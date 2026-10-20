using System.Globalization;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Finds the operation for an <c>invoke</c>, parses its arguments on the calling thread, runs it on EPLAN's main
/// thread through the dispatcher and converts every outcome into data or a <see cref="BridgeErrorException"/>.
/// Writes (<see cref="IWriteOperation"/>) also go through <see cref="OutcomeRecords"/>, and <c>outcome.get</c> is
/// answered on the calling pipe thread from them (ADR-0010). Thread-safe; one router serves every connection.
/// </summary>
public sealed class OperationRouter
{
    private readonly Dictionary<string, IOperation> _operations;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly IExceptionMapper? _exceptionMapper;
    private readonly IBridgeLog _log;
    private readonly TimeSpan _startTimeout;
    private readonly OutcomeRecords _outcomes = new();

    public OperationRouter(IEnumerable<IOperation> operations, IMainThreadDispatcher dispatcher, IExceptionMapper? exceptionMapper, IBridgeLog log, TimeSpan startTimeout)
    {
        _operations = operations.ToDictionary(o => o.Name, StringComparer.Ordinal);
        _dispatcher = dispatcher;
        _exceptionMapper = exceptionMapper;
        _log = log;
        _startTimeout = startTimeout;
    }

    /// <summary>The names of the registered operations and <c>outcome.get</c>, sorted; announced in the handshake.</summary>
    public IReadOnlyList<string> SupportedOperations =>
        [.. _operations.Keys.Append(OperationNames.OutcomeGet).OrderBy(name => name, StringComparer.Ordinal)];

    public JToken Invoke(string operationName, JToken? args, string correlationId)
    {
        if (operationName == OperationNames.OutcomeGet)
        {
            // On this pipe thread, never the main thread: it must answer while a write is running.
            return BridgeJson.FromObject(_outcomes.Get(RequestIdOf(args)));
        }

        if (!_operations.TryGetValue(operationName, out IOperation? operation))
        {
            throw new RpcException(RpcException.InvalidParams, $"Unknown operation '{operationName}'.");
        }

        object parsed = operation.ParseArgs(args);
        return operation is IWriteOperation ? InvokeWrite(operation, parsed, correlationId) : InvokeRead(operation, parsed, correlationId);
    }

    private JToken InvokeRead(IOperation operation, object parsed, string correlationId)
    {
        string operationName = operation.Name;
        DispatchResult outcome = _dispatcher.Run(() => operation.Execute(parsed), _startTimeout);
        switch (outcome.Status)
        {
            case DispatchStatus.Completed:
                return BridgeJson.FromObject(outcome.Value);
            case DispatchStatus.Busy:
                _log.Write(BridgeLogLevel.Information, $"[{correlationId}] {operationName} refused: {outcome.BusyReason}");
                throw Busy(outcome.BusyReason);
            default:
                throw Failure(outcome.Exception!, operationName, correlationId);
        }
    }

    /// <summary>
    /// A write: recorded as queued, then running once on the main thread, then with its final state before the
    /// response leaves (ADR-0010: record first, then respond). Every error says whether the project changed.
    /// </summary>
    private JToken InvokeWrite(IOperation operation, object parsed, string requestId)
    {
        if (!_outcomes.TryBegin(requestId, operation.Name, out OutcomeGetData? previous))
        {
            _log.Write(BridgeLogLevel.Warning, $"[{requestId}] {operation.Name} refused: the request ID was used before ({previous!.Status})");
            throw Stamped(
                new BridgeErrorException(
                    ErrorReason.InvalidArgument,
                    "This request ID was used before, and a write never runs twice. Ask outcome.get for the outcome of the first request.",
                    retryable: false,
                    new Dictionary<string, string> { ["requestId"] = requestId, ["outcome"] = previous.Status }),
                ExecutionState.NotStarted);
        }

        DispatchResult outcome = _dispatcher.Run(
            () =>
            {
                _outcomes.Update(requestId, OutcomeStatus.Running);
                return operation.Execute(parsed);
            },
            _startTimeout);
        switch (outcome.Status)
        {
            case DispatchStatus.Completed:
                _outcomes.Update(requestId, OutcomeStatus.Committed);
                return BridgeJson.FromObject(outcome.Value);
            case DispatchStatus.Busy:
                // An abandoned dispatch never runs later (api-notes A9), so nothing was changed.
                _outcomes.Update(requestId, OutcomeStatus.NotStarted, ErrorReason.EplanBusy);
                _log.Write(BridgeLogLevel.Information, $"[{requestId}] {operation.Name} refused: {outcome.BusyReason}");
                throw Stamped(Busy(outcome.BusyReason), ExecutionState.NotStarted);
            default:
                BridgeErrorException failure = Failure(outcome.Exception!, operation.Name, requestId);
                string execution = failure.Error.Execution ?? ExecutionState.RolledBack;
                _outcomes.Update(requestId, execution, failure.Error.Reason);
                throw Stamped(failure, execution);
        }
    }

    private static BridgeErrorException Stamped(BridgeErrorException error, string execution)
    {
        error.Error.Execution = execution;
        return error;
    }

    private static string RequestIdOf(JToken? args) =>
        args is JObject { } parameters && parameters["requestId"] is { Type: JTokenType.String } id && ((string)id!).Length > 0
            ? (string)id!
            : throw new BridgeErrorException(ErrorReason.InvalidArgument, "outcome.get needs {requestId}: the correlationId of the write's invoke.");

    private BridgeErrorException Busy(BusyReason reason)
    {
        string seconds = _startTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        string message = reason switch
        {
            BusyReason.ModalDialog => "EPLAN is showing a dialog. Ask the engineer to close open dialogs in EPLAN, then retry.",
            BusyReason.Reentrant => "Another EplanMcp operation is still running in EPLAN. Retry in a moment.",
            _ => $"EPLAN did not respond within {seconds} s; it may be busy with a long task. Ask the engineer to let it finish, then retry.",
        };
        return new BridgeErrorException(ErrorReason.EplanBusy, message, retryable: true, new Dictionary<string, string> { ["busyReason"] = reason.ToString() });
    }

    private BridgeErrorException Failure(Exception exception, string operationName, string correlationId)
    {
        if (exception is BridgeErrorException known)
        {
            return known;
        }

        BridgeError? mapped = _exceptionMapper?.Map(exception);
        if (mapped is not null)
        {
            _log.Write(BridgeLogLevel.Warning, $"[{correlationId}] {operationName} failed: {mapped.Reason}", exception);
            return new BridgeErrorException(mapped.Reason, mapped.Message, mapped.Retryable, mapped.Metadata);
        }

        _log.Write(BridgeLogLevel.Error, $"[{correlationId}] {operationName} failed unexpectedly", exception);
        return new BridgeErrorException(
            ErrorReason.Internal,
            $"An internal error occurred in the EplanMcp add-in (correlation ID {correlationId}). Please report it.",
            retryable: false,
            new Dictionary<string, string> { ["correlationId"] = correlationId });
    }
}

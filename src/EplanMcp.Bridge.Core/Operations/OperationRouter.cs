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
/// answered on the calling pipe thread from them (ADR-0010). Every invoke runs with an <see cref="OperationContext"/>,
/// through which the operation reports progress and which <c>operation.cancel</c> flags from another pipe thread (M4
/// design 4.3). Thread-safe; one router serves every connection.
/// </summary>
public sealed class OperationRouter
{
    private readonly Dictionary<string, IOperation> _operations;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly IExceptionMapper? _exceptionMapper;
    private readonly IBridgeLog _log;
    private readonly TimeSpan _startTimeout;
    private readonly OutcomeRecords _outcomes = new();
    private readonly RunningOperations _running = new();

    public OperationRouter(IEnumerable<IOperation> operations, IMainThreadDispatcher dispatcher, IExceptionMapper? exceptionMapper, IBridgeLog log, TimeSpan startTimeout)
    {
        _operations = operations.ToDictionary(o => o.Name, StringComparer.Ordinal);
        _dispatcher = dispatcher;
        _exceptionMapper = exceptionMapper;
        _log = log;
        _startTimeout = startTimeout;
    }

    /// <summary>
    /// The names of the registered operations, <c>outcome.get</c> and <c>operation.cancel</c>, sorted; announced in the
    /// handshake.
    /// </summary>
    public IReadOnlyList<string> SupportedOperations =>
        [.. _operations.Keys.Append(OperationNames.OutcomeGet).Append(OperationNames.OperationCancel).OrderBy(name => name, StringComparer.Ordinal)];

    public JToken Invoke(string operationName, JToken? args, string correlationId) => Invoke(operationName, args, correlationId, null);

    /// <summary>Runs one invoke; the operation's progress goes to <paramref name="progress"/>, or nowhere when it is null.</summary>
    internal JToken Invoke(string operationName, JToken? args, string correlationId, ProgressSlot? progress)
    {
        // These two on this pipe thread, never the main thread: they must answer while a write is running.
        if (operationName == OperationNames.OutcomeGet)
        {
            return BridgeJson.FromObject(_outcomes.Get(RequestIdOf(args, operationName)));
        }

        if (operationName == OperationNames.OperationCancel)
        {
            string requestId = RequestIdOf(args, operationName);
            bool found = _running.Cancel(requestId);
            _log.Write(BridgeLogLevel.Information, $"[{requestId}] cancel requested: {(found ? "it stops at its next safe point" : "not queued or running")}");
            return BridgeJson.FromObject(new OperationCancelData { Found = found });
        }

        if (!_operations.TryGetValue(operationName, out IOperation? operation))
        {
            throw new RpcException(RpcException.InvalidParams, $"Unknown operation '{operationName}'.");
        }

        object parsed = operation.ParseArgs(args);
        bool write = operation is IWriteOperation;
        var context = new OperationContext(correlationId, write, progress);
        _running.Add(context);
        try
        {
            return write ? InvokeWrite(operation, parsed, context) : InvokeRead(operation, parsed, context);
        }
        finally
        {
            _running.Remove(context);
        }
    }

    private JToken InvokeRead(IOperation operation, object parsed, OperationContext context)
    {
        string operationName = operation.Name;
        string correlationId = context.RequestId;
        DispatchResult outcome = _dispatcher.Run(
            () => context.Run(() =>
            {
                // The first safe point: an invoke cancelled while it waited for the main thread does nothing.
                context.ThrowIfCancellationRequested();
                return operation.Execute(parsed);
            }),
            _startTimeout);
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
    private JToken InvokeWrite(IOperation operation, object parsed, OperationContext context)
    {
        string requestId = context.RequestId;
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
            () => context.Run(() =>
            {
                context.ThrowIfCancellationRequested(); // not_started: the write never began
                _outcomes.Update(requestId, OutcomeStatus.Running);
                return operation.Execute(parsed);
            }),
            _startTimeout);
        switch (outcome.Status)
        {
            case DispatchStatus.Completed:
                _outcomes.Update(requestId, outcome.Value is IWriteExecution data ? data.Execution : OutcomeStatus.Committed);
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

    private static string RequestIdOf(JToken? args, string operationName) =>
        args is JObject { } parameters && parameters["requestId"] is { Type: JTokenType.String } id && ((string)id!).Length > 0
            ? (string)id!
            : throw new BridgeErrorException(ErrorReason.InvalidArgument, operationName + " needs {requestId}: the correlationId of the invoke it is about.");

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
            if (known.Error.Reason == ErrorReason.Cancelled)
            {
                _log.Write(BridgeLogLevel.Information, $"[{correlationId}] {operationName} cancelled ({known.Error.Execution ?? "a read"})");
            }
            else if (known.InnerException is not null)
            {
                _log.Write(BridgeLogLevel.Warning, $"[{correlationId}] {operationName} failed: {known.Error.Reason}", known.InnerException);
            }

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

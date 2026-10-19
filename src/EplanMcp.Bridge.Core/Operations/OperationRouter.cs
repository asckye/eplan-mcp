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
/// Thread-safe: holds no per-call state.
/// </summary>
public sealed class OperationRouter
{
    private readonly Dictionary<string, IOperation> _operations;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly IExceptionMapper? _exceptionMapper;
    private readonly IBridgeLog _log;
    private readonly TimeSpan _startTimeout;

    public OperationRouter(IEnumerable<IOperation> operations, IMainThreadDispatcher dispatcher, IExceptionMapper? exceptionMapper, IBridgeLog log, TimeSpan startTimeout)
    {
        _operations = operations.ToDictionary(o => o.Name, StringComparer.Ordinal);
        _dispatcher = dispatcher;
        _exceptionMapper = exceptionMapper;
        _log = log;
        _startTimeout = startTimeout;
    }

    /// <summary>The names of the registered operations, sorted; announced in the handshake.</summary>
    public IReadOnlyList<string> SupportedOperations => [.. _operations.Keys.OrderBy(name => name, StringComparer.Ordinal)];

    public JToken Invoke(string operationName, JToken? args, string correlationId)
    {
        if (!_operations.TryGetValue(operationName, out IOperation? operation))
        {
            throw new RpcException(RpcException.InvalidParams, $"Unknown operation '{operationName}'.");
        }

        object parsed = operation.ParseArgs(args);
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

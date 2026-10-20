using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// Writes (ADR-0010 section 2, M3 design 4.3): the caller's request ID goes with the invoke, which is never sent
/// twice. When the response does not come (call timeout, lost connection, the caller cancelling), the client asks the
/// same Bridge, recognised by its pipe name, with outcome.get every <see cref="_pollInterval"/> until the write's
/// execution limit, and reports what it recorded; when that cannot be learned, OUTCOME_UNKNOWN.
/// </summary>
internal sealed partial class BridgeClient
{
    public async Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)limit.TotalMilliseconds;
        if (!await _queue.WaitAsync(_queueTimeout, cancellationToken).ConfigureAwait(false))
        {
            throw Stamped(BridgeErrors.QueueBusy(), ExecutionState.NotStarted);
        }

        try
        {
            BridgeEndpoint target;
            try
            {
                HandshakeResult handshake = await EnsureConnectedAsync(Discover(), null, cancellationToken).ConfigureAwait(false);
                target = _channel!.Endpoint;
                if (!Supports(handshake, operation))
                {
                    throw new BridgeCallException(BridgeErrors.OperationMissing(operation, handshake.BridgeVersion));
                }

                if (!Supports(handshake, OperationNames.OutcomeGet))
                {
                    throw new BridgeCallException(BridgeErrors.WritesUnsupported(handshake.BridgeVersion));
                }
            }
            catch (BridgeCallException ex)
            {
                throw Stamped(ex.Error, ExecutionState.NotStarted);
            }

            var parameters = new JsonObject
            {
                ["operation"] = operation,
                ["correlationId"] = requestId,
                ["args"] = JsonSerializer.SerializeToNode(args, HostJson.Options),
            };
            LogInvoke(operation, requestId);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(limit < _callTimeout ? limit : _callTimeout);
            BridgeError interruption;
            try
            {
                JsonNode? result = await _channel!.CallAsync(BridgeProtocol.InvokeMethod, parameters, wait.Token).ConfigureAwait(false);
                return new WriteResult<TData>(Committed<TData>(result, requestId), Recovered: false);
            }
            catch (BridgeRpcException ex)
            {
                // The Bridge answered; it stamps every error of a write. One it did not stamp never reached the write.
                BridgeError error = Map(ex, operation).Error;
                throw Stamped(error, error.Execution ?? ExecutionState.NotStarted);
            }
            catch (OperationCanceledException)
            {
                interruption = BridgeErrors.Timeout(_callTimeout);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                LogConnectionLost(ex);
                interruption = BridgeErrors.ConnectionLost();
            }

            // A late response must not reach the next call, and this channel may be broken anyway.
            await DropAsync().ConfigureAwait(false);
            LogWriteInterrupted(operation, requestId, interruption.Reason);
            return await RecoverAsync<TData>(target, requestId, interruption, deadline).ConfigureAwait(false);
        }
        finally
        {
            _queue.Release();
        }
    }

    private async Task<WriteResult<TData>> RecoverAsync<TData>(BridgeEndpoint target, string requestId, BridgeError interruption, long deadline)
    {
        while (true)
        {
            (OutcomeGetData? outcome, bool gone) = await FetchOutcomeAsync(target, requestId).ConfigureAwait(false);
            bool timeLeft = Environment.TickCount64 < deadline;
            switch (outcome?.Status)
            {
                case OutcomeStatus.Committed:
                    LogOutcomeFetched(requestId, OutcomeStatus.Committed);
                    return new WriteResult<TData>(default, Recovered: true);
                case OutcomeStatus.RolledBack:
                    LogOutcomeFetched(requestId, OutcomeStatus.RolledBack);
                    throw Stamped(BridgeErrors.RolledBackAfterInterruption(outcome.Reason), ExecutionState.RolledBack);
                case OutcomeStatus.NotStarted:
                    LogOutcomeFetched(requestId, OutcomeStatus.NotStarted);

                    // No operation recorded: the request never arrived, so the interruption itself is the error.
                    throw Stamped(outcome.Operation is null ? interruption : BridgeErrors.NotStartedAfterInterruption(outcome.Reason), ExecutionState.NotStarted);
                case OutcomeStatus.Queued or OutcomeStatus.Running when timeLeft:
                case null when !gone && timeLeft:
                    await Task.Delay(_pollInterval).ConfigureAwait(false);
                    continue;
                default:
                    LogOutcomeUnknown(requestId, outcome?.Status ?? (gone ? "bridge gone" : "unreachable"));
                    throw Stamped(BridgeErrors.OutcomeUnknown(), ExecutionState.Unknown);
            }
        }
    }

    /// <summary>
    /// outcome.get on <paramref name="target"/> only. Gone: that Bridge no longer exists (EPLAN exited or restarted
    /// under another pipe name); null data and not gone: it could not be asked this time.
    /// </summary>
    private async Task<(OutcomeGetData? Outcome, bool Gone)> FetchOutcomeAsync(BridgeEndpoint target, string requestId)
    {
        IReadOnlyList<BridgeEndpoint> instances = Discover();
        if (!instances.Contains(target))
        {
            return (null, true);
        }

        using var timeout = new CancellationTokenSource(_callTimeout);
        try
        {
            await EnsureConnectedAsync(instances, target.ProcessId, timeout.Token).ConfigureAwait(false);
            if (_channel?.Endpoint != target)
            {
                return (null, true);
            }

            var parameters = new JsonObject
            {
                ["operation"] = OperationNames.OutcomeGet,
                ["correlationId"] = Guid.NewGuid().ToString("N"),
                ["args"] = JsonSerializer.SerializeToNode(new OutcomeGetArgs { RequestId = requestId }, HostJson.Options),
            };
            JsonNode? result = await CallAsync(BridgeProtocol.InvokeMethod, parameters, timeout.Token).ConfigureAwait(false);
            return (result?["data"]?.Deserialize<OutcomeGetData>(HostJson.Options), false);
        }
        catch (Exception ex) when (ex is BridgeCallException or OperationCanceledException or JsonException)
        {
            await DropAsync().ConfigureAwait(false);
            return (null, false);
        }
    }

    /// <summary>The data of a committed write; unreadable data is our defect after the commit (INTERNAL, committed).</summary>
    private TData Committed<TData>(JsonNode? result, string requestId)
    {
        try
        {
            return result?["data"] is { } data && data.Deserialize<TData>(HostJson.Options) is { } typed
                ? typed
                : throw new JsonException("The invoke result has no data.");
        }
        catch (JsonException ex)
        {
            LogUnexpected(ex, requestId);
            throw Stamped(BridgeErrors.Internal(requestId), ExecutionState.Committed);
        }
    }

    private static BridgeCallException Stamped(BridgeError error, string execution)
    {
        error.Execution = execution;
        Debug.Assert(ExecutionInvariants.Hold(error), $"ADR-0010 invariants broken by {error.Reason}/{execution}");
        return new BridgeCallException(error);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Write {Operation} ({RequestId}) interrupted: {Reason}; fetching its outcome")]
    private partial void LogWriteInterrupted(string operation, string requestId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outcome of write {RequestId} fetched: {Status}")]
    private partial void LogOutcomeFetched(string requestId, string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outcome of write {RequestId} unknown ({Why})")]
    private partial void LogOutcomeUnknown(string requestId, string why);
}

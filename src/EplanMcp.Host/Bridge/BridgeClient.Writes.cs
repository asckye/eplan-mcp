using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// Writes (ADR-0010 section 2, M3 design 4.3): the caller's request ID goes with the invoke, which is never sent
/// twice. The response is waited for until the write's execution limit, not the read call timeout: a write may run for
/// minutes (numbering every terminal of a project took 43 s, a backup included), and its full result comes only with
/// the response. When it does not come (the limit passed, lost connection, the caller cancelling), the client asks the
/// same Bridge, recognised by its pipe name, with outcome.get every <see cref="_pollInterval"/> until that limit, and
/// at least once, and reports what it recorded; when that cannot be learned, OUTCOME_UNKNOWN. When the caller
/// cancelled, the Bridge is first asked to stop the write at its next safe point (M4 design 4.3); a timeout does not
/// stop it.
/// </summary>
internal sealed partial class BridgeClient
{
    public async Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)limit.TotalMilliseconds;
        bool entered;
        try
        {
            entered = await _queue.WaitAsync(_queueTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw Stamped(BridgeErrors.Cancelled(), ExecutionState.NotStarted); // still in the queue: never sent
        }

        if (!entered)
        {
            throw Stamped(BridgeErrors.QueueBusy(), ExecutionState.NotStarted);
        }

        try
        {
            BridgeEndpoint target;
            bool cancellable;
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

                // An older add-in cannot stop a write: a cancelled one is still waited for.
                cancellable = Supports(handshake, OperationNames.OperationCancel);
            }
            catch (BridgeCallException ex)
            {
                throw Stamped(ex.Error, ExecutionState.NotStarted);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw Stamped(BridgeErrors.Cancelled(), ExecutionState.NotStarted);
            }

            LogInvoke(operation, requestId);
            // The queue and the handshake count against the limit too, so the wait ends at the write's deadline.
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64)));
            BridgeError interruption;
            try
            {
                JsonNode? result = await _channel!.CallAsync(BridgeProtocol.InvokeMethod, Envelope(operation, requestId, args), ForRequest(requestId, progress), wait.Token).ConfigureAwait(false);
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
                interruption = cancellationToken.IsCancellationRequested ? BridgeErrors.Cancelled() : BridgeErrors.Timeout(limit);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                LogConnectionLost(ex);
                interruption = BridgeErrors.ConnectionLost();
            }

            // A late response must not reach the next call, and this channel may be broken anyway.
            await DropAsync().ConfigureAwait(false);
            LogWriteInterrupted(operation, requestId, interruption.Reason);
            return await RecoverAsync<TData>(target, requestId, interruption, deadline, cancellable && cancellationToken.IsCancellationRequested).ConfigureAwait(false);
        }
        finally
        {
            _queue.Release();
        }
    }

    public async Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken)
    {
        if (!await _queue.WaitAsync(_queueTimeout, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return Discover().FirstOrDefault(e => e.ProcessId == processId) is { } target
                ? (await FetchOutcomeAsync(target, requestId).ConfigureAwait(false)).Data
                : null;
        }
        finally
        {
            _queue.Release();
        }
    }

    /// <summary>Polls outcome.get; with <paramref name="cancel"/>, sends operation.cancel until the Bridge has found the write.</summary>
    private async Task<WriteResult<TData>> RecoverAsync<TData>(BridgeEndpoint target, string requestId, BridgeError interruption, long deadline, bool cancel)
    {
        bool stopping = false;
        while (true)
        {
            if (cancel && !stopping)
            {
                // Not found yet may mean not read yet: the next outcome.get then says queued or running, or not_started,
                // which keeps it from ever running.
                stopping = await CancelAsync(target, requestId).ConfigureAwait(false);
            }

            (OutcomeGetData? outcome, bool gone) = await FetchOutcomeAsync(target, requestId).ConfigureAwait(false);
            bool timeLeft = Environment.TickCount64 < deadline;
            switch (outcome?.Status)
            {
                case OutcomeStatus.Committed:
                    LogOutcomeFetched(requestId, OutcomeStatus.Committed);
                    return new WriteResult<TData>(default, Recovered: true);
                case OutcomeStatus.RolledBack:
                    LogOutcomeFetched(requestId, OutcomeStatus.RolledBack);
                    throw Stamped(outcome.Reason == ErrorReason.Cancelled ? BridgeErrors.Cancelled() : BridgeErrors.RolledBackAfterInterruption(outcome.Reason), ExecutionState.RolledBack);
                case OutcomeStatus.NotStarted:
                    LogOutcomeFetched(requestId, OutcomeStatus.NotStarted);

                    if (outcome.Operation is not null && outcome.Reason is null)
                    {
                        return new WriteResult<TData>(default, Recovered: true, ExecutionState.NotStarted);
                    }

                    // No operation recorded: the request never arrived, so the interruption itself is the error.
                    throw Stamped(
                        outcome.Operation is null ? interruption
                            : outcome.Reason == ErrorReason.Cancelled ? BridgeErrors.Cancelled()
                            : BridgeErrors.NotStartedAfterInterruption(outcome.Reason),
                        ExecutionState.NotStarted);
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

    private Task<(OutcomeGetData? Data, bool Gone)> FetchOutcomeAsync(BridgeEndpoint target, string requestId) =>
        AskAsync<OutcomeGetArgs, OutcomeGetData>(target, OperationNames.OutcomeGet, new OutcomeGetArgs { RequestId = requestId });

    /// <summary>
    /// operation.cancel for <paramref name="requestId"/> on <paramref name="target"/>; true when the Bridge found the
    /// invoke queued or running and stops it at its next safe point, false when it had none or could not be asked.
    /// </summary>
    private async Task<bool> CancelAsync(BridgeEndpoint target, string requestId)
    {
        bool found = (await AskAsync<OperationCancelArgs, OperationCancelData>(target, OperationNames.OperationCancel, new OperationCancelArgs { RequestId = requestId }).ConfigureAwait(false)).Data?.Found == true;
        LogCancelSent(requestId, found);
        return found;
    }

    /// <summary>
    /// An operation the Bridge answers on its pipe thread while an invoke runs (outcome.get, operation.cancel), on
    /// <paramref name="target"/> only. Gone: that Bridge no longer exists (EPLAN exited or restarted under another pipe
    /// name); null data and not gone: it could not be asked this time.
    /// </summary>
    private async Task<(TData? Data, bool Gone)> AskAsync<TArgs, TData>(BridgeEndpoint target, string operation, TArgs args)
        where TData : class
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

            JsonNode? result = await CallAsync(BridgeProtocol.InvokeMethod, Envelope(operation, Guid.NewGuid().ToString("N"), args), null, timeout.Token).ConfigureAwait(false);
            return (result?["data"]?.Deserialize<TData>(HostJson.Options), false);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Asked the Bridge to cancel {RequestId}; found: {Found}")]
    private partial void LogCancelSent(string requestId, bool found);
}

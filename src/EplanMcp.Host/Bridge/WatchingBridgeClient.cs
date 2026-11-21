using EplanMcp.Contracts;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// The Bridge client with the EPLAN window watch in front (ADR-0063 decision 6): a call that finds EPLAN busy behind a
/// modal window gets one look at EPLAN's windows. Windows on the harmless list are closed, and the error says what was
/// closed or which other window is in the way. The call itself is never repeated here: EPLAN_BUSY stays retryable for
/// the client, and a write is never sent twice (ADR-0037 decision 4).
/// </summary>
internal sealed class WatchingBridgeClient(IBridgeClient inner, WindowWatch watch) : IBridgeClient
{
    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);

    public Task<BridgeStatus> GetStatusAsync(bool refresh, CancellationToken cancellationToken) => inner.GetStatusAsync(refresh, cancellationToken);

    public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => inner.SelectAsync(processId, cancellationToken);

    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken) =>
        WatchedAsync(() => inner.InvokeAsync<TArgs, TData>(operation, args, cancellationToken), operation, cancellationToken);

    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) =>
        WatchedAsync(() => inner.InvokeAsync<TArgs, TData>(operation, args, timeout, progress, cancellationToken), operation, cancellationToken);

    public Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) =>
        WatchedAsync(() => inner.InvokeWriteAsync<TArgs, TData>(operation, args, requestId, limit, progress, cancellationToken), operation, cancellationToken);

    public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) =>
        inner.FetchOutcomeAsync(processId, requestId, cancellationToken);

    private async Task<T> WatchedAsync<T>(Func<Task<T>> call, string operation, CancellationToken cancellationToken)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (BridgeCallException ex) when (ex.Error.Reason == ErrorReason.EplanBusy)
        {
            int? processId = (await inner.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake?.Eplan.ProcessId;
            WindowWatchResult result = processId is { } id ? await watch.SweepAsync(id, operation, cancellationToken).ConfigureAwait(false) : WindowWatchResult.Nothing;
            if (result.Describe() is { Length: > 0 } said)
            {
                ex.Error.Message = (ex.Error.Message.TrimEnd() + " " + said + (result.Closed.Count > 0 ? " Call again." : string.Empty)).Trim();
            }

            throw;
        }
    }
}

using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>The Host's only way to reach EPLAN. Tools depend on this interface; tests replace it with fakes.</summary>
internal interface IBridgeClient
{
    /// <summary>Discovers Bridges and handshakes with the one in use. Never throws for "not connected" states.</summary>
    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>With refresh, obtains a new handshake instead of trusting the cached licence and operations.</summary>
    public Task<BridgeStatus> GetStatusAsync(bool refresh, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the EPLAN running as <paramref name="processId"/> the one every later call uses, once a handshake with it
    /// succeeds; until then the previous choice stays. Throws <see cref="BridgeCallException"/>: INVALID_ARGUMENT when
    /// no Bridge runs in that process, otherwise as a handshake fails.
    /// </summary>
    public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one Bridge operation; throws <see cref="BridgeCallException"/> carrying the tool error. When
    /// <paramref name="cancellationToken"/> fires after the operation was sent, the Bridge is asked to stop it at its
    /// next safe point (operation.cancel, M4 design 4.3) and <see cref="OperationCanceledException"/> follows.
    /// </summary>
    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="InvokeAsync{TArgs, TData}(string, TArgs, CancellationToken)"/> waiting up to <paramref name="timeout"/>
    /// instead of the usual call timeout, for an operation that waits for the engineer (approval.ask) or runs long.
    /// The operation's progress reports go to <paramref name="progress"/> while its response is awaited.
    /// </summary>
    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a write's execute operation under <paramref name="requestId"/>, never sending it twice (ADR-0010), with its
    /// progress reports going to <paramref name="progress"/>. The response is awaited until <paramref name="limit"/>;
    /// after that, a lost connection or cancellation it fetches the outcome with outcome.get until the limit, at least
    /// once; on cancellation it first asks the Bridge to
    /// stop the write at its next safe point (M4 design 4.3), and a write that stopped fails with CANCELLED. Throws
    /// <see cref="BridgeCallException"/> whose error carries <c>Execution</c>; OUTCOME_UNKNOWN when the outcome cannot be
    /// learned.
    /// </summary>
    public Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// What the Bridge in EPLAN process <paramref name="processId"/> recorded for <paramref name="requestId"/>, for a
    /// write another Host process started (ADR-0010 section 2); null when that Bridge cannot be asked.
    /// </summary>
    public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken);
}

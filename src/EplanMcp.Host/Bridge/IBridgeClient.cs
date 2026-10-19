using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>The Host's only way to reach EPLAN. Tools depend on this interface; tests replace it with fakes.</summary>
internal interface IBridgeClient
{
    /// <summary>Discovers Bridges and handshakes with the one in use. Never throws for "not connected" states.</summary>
    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Makes the EPLAN running as <paramref name="processId"/> the one every later call uses, once a handshake with it
    /// succeeds; until then the previous choice stays. Throws <see cref="BridgeCallException"/>: INVALID_ARGUMENT when
    /// no Bridge runs in that process, otherwise as a handshake fails.
    /// </summary>
    public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken);

    /// <summary>Runs one Bridge operation; throws <see cref="BridgeCallException"/> carrying the tool error.</summary>
    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken);
}

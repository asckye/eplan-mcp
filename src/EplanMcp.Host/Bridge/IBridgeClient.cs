namespace EplanMcp.Host.Bridge;

/// <summary>The Host's only way to reach EPLAN. Tools depend on this interface; tests replace it with fakes.</summary>
internal interface IBridgeClient
{
    /// <summary>Discovers Bridges and handshakes with the single one. Never throws for "not connected" states.</summary>
    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Runs one Bridge operation; throws <see cref="BridgeCallException"/> carrying the tool error.</summary>
    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken);
}

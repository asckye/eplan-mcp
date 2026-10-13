namespace EplanMcp.Host.Bridge;

/// <summary>Finds running Bridges. Cheap enough to call before every tool call.</summary>
internal interface IBridgeDiscovery
{
    public IReadOnlyList<BridgeEndpoint> Discover();
}

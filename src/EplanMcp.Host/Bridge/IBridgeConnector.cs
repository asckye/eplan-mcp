namespace EplanMcp.Host.Bridge;

/// <summary>Opens a channel to a discovered Bridge.</summary>
internal interface IBridgeConnector
{
    /// <summary>Throws <see cref="TimeoutException"/>, <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
    public Task<IBridgeChannel> ConnectAsync(BridgeEndpoint endpoint, CancellationToken cancellationToken);
}

using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Addin;

/// <summary>
/// The Bridge in an EPLAN that install-addin started, seen through its pipe: whether it is up, and the handshake, which
/// says where EPLAN registered the add-in it loaded (<see cref="HandshakeResult.AddinPath"/>). Production:
/// <see cref="BridgeProbe"/>; tests fake it.
/// </summary>
internal interface IBridgeProbe
{
    public bool IsUp(int processId);

    /// <summary>The handshake with the Bridge in <paramref name="processId"/>, or null when there is none.</summary>
    public Task<HandshakeResult?> HandshakeAsync(int processId, CancellationToken cancellationToken);
}

/// <summary>The Bridge pipes as the Host finds them (<see cref="PipeBridgeDiscovery"/>), with a handshake as every Host makes it.</summary>
internal sealed class BridgeProbe(IBridgeDiscovery discovery, IBridgeConnector connector) : IBridgeProbe
{
    public bool IsUp(int processId)
    {
        try
        {
            return discovery.Discover().Any(e => e.ProcessId == processId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // looked at again at the next poll
        }
    }

    public async Task<HandshakeResult?> HandshakeAsync(int processId, CancellationToken cancellationToken)
    {
        var client = new BridgeClient(discovery, connector, NullLogger<BridgeClient>.Instance);
        await using (client.ConfigureAwait(false))
        {
            try
            {
                return await client.SelectAsync(processId, cancellationToken).ConfigureAwait(false);
            }
            catch (BridgeCallException)
            {
                return null;
            }
        }
    }
}

using System.IO.Pipes;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// Connects with <see cref="PipeOptions.CurrentUserOnly"/>, which also verifies that the pipe's server runs as the
/// current user (SEC-03): a pipe squatted by another account is refused.
/// </summary>
internal sealed class PipeBridgeConnector : IBridgeConnector
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    public async Task<IBridgeChannel> ConnectAsync(BridgeEndpoint endpoint, CancellationToken cancellationToken)
    {
        NamedPipeClientStream? pipe = new(".", endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
            var channel = new PipeBridgeChannel(pipe, endpoint);
            pipe = null; // owned by the channel now
            return channel;
        }
        finally
        {
            pipe?.Dispose();
        }
    }
}

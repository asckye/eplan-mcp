using System.Text.Json.Nodes;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// One JSON-RPC connection to a Bridge. Not thread-safe: <see cref="BridgeClient"/> serializes all calls.
/// Throws <see cref="BridgeRpcException"/> for an error response and <see cref="IOException"/> when the
/// connection breaks; after either kind of I/O failure the channel must be discarded.
/// </summary>
internal interface IBridgeChannel : IAsyncDisposable
{
    public BridgeEndpoint Endpoint { get; }

    public Task<JsonNode?> CallAsync(string method, JsonNode? parameters, CancellationToken cancellationToken);
}

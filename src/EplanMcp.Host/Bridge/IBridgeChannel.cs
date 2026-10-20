using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// One JSON-RPC connection to a Bridge. Not thread-safe: <see cref="BridgeClient"/> serializes all calls.
/// Throws <see cref="BridgeRpcException"/> for an error response and <see cref="IOException"/> when the
/// connection breaks; after either kind of I/O failure the channel must be discarded.
/// </summary>
internal interface IBridgeChannel : IAsyncDisposable
{
    public BridgeEndpoint Endpoint { get; }

    /// <summary>
    /// Sends <paramref name="method"/> and returns its result. The <c>progress</c> notifications that arrive before the
    /// response go to <paramref name="progress"/>, on the reading thread; other notifications are skipped.
    /// </summary>
    public Task<JsonNode?> CallAsync(string method, JsonNode? parameters, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken);
}

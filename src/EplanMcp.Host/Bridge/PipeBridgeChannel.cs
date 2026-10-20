using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>JSON-RPC 2.0 over a connected named pipe client stream. Calls must not overlap.</summary>
internal sealed class PipeBridgeChannel(NamedPipeClientStream pipe, BridgeEndpoint endpoint) : IBridgeChannel
{
    private long _nextId;

    public BridgeEndpoint Endpoint => endpoint;

    public async Task<JsonNode?> CallAsync(string method, JsonNode? parameters, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
    {
        long id = Interlocked.Increment(ref _nextId);
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
        await Framing.WriteFrameAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request, HostJson.Options), cancellationToken).ConfigureAwait(false);

        while (true)
        {
            JsonNode? message;
            try
            {
                message = JsonNode.Parse(await Framing.ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false));
            }
            catch (JsonException ex)
            {
                throw new IOException("The Bridge sent invalid JSON.", ex);
            }

            if (message is not JsonObject response || response["id"] is null)
            {
                // A notification: not ours to answer. Only an invoke's progress is of use (M4 design 4.3).
                if (progress is not null && message is JsonObject notification && Progress(notification) is { } report)
                {
                    progress.Report(report);
                }

                continue;
            }

            if (response["id"]!.GetValueKind() != JsonValueKind.Number || response["id"]!.GetValue<long>() != id)
            {
                throw new IOException("The Bridge answered a different request.");
            }

            if (response["error"] is JsonObject error)
            {
                int code = error["code"]?.GetValueKind() == JsonValueKind.Number ? error["code"]!.GetValue<int>() : 0;
                throw new BridgeRpcException(code, error["message"]?.ToString() ?? "error", error["data"]?.DeepClone());
            }

            return response["result"]?.DeepClone();
        }
    }

    public ValueTask DisposeAsync() => pipe.DisposeAsync();

    /// <summary>
    /// The report of a <c>progress</c> notification; null for any other notification, and for a malformed one, which
    /// must not fail the call it arrived during.
    /// </summary>
    private static ProgressNotification? Progress(JsonObject notification)
    {
        if (notification["method"] is not { } method || method.GetValueKind() != JsonValueKind.String || method.GetValue<string>() != BridgeProtocol.ProgressMethod)
        {
            return null;
        }

        try
        {
            return notification["params"]?.Deserialize<ProgressNotification>(HostJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>JSON-RPC 2.0 over a connected named pipe client stream. Calls must not overlap.</summary>
internal sealed class PipeBridgeChannel(Stream pipe, BridgeEndpoint endpoint) : IBridgeChannel
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
                message = ParseMessage(await Framing.ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false));
            }
            catch (JsonException ex)
            {
                throw new IOException("The Bridge sent invalid JSON.", ex);
            }

            if (message is JsonObject uncorrelated && uncorrelated["id"] is null && uncorrelated["error"] is JsonObject protocolError)
            {
                throw RpcError(protocolError, invalidFrame: true);
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
                throw RpcError(error);
            }

            return response["result"]?.DeepClone();
        }
    }

    public ValueTask DisposeAsync() => pipe.DisposeAsync();

    private static JsonNode? ParseMessage(byte[] frame)
    {
        using JsonDocument document = JsonDocument.Parse(frame);
        JsonElement root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("method", out _))
            {
                ValidateMembers(root, "jsonrpc", "method", "params");
            }
            else
            {
                ValidateMembers(root, "jsonrpc", "id", "result", "error");
            }

            if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
            {
                ValidateMembers(error, "code", "message", "data");
            }
        }

        return JsonNode.Parse(frame);
    }

    private static void ValidateMembers(JsonElement value, params string[] allowed)
    {
        // JsonNode defers duplicate detection; inspect decoded wire names before any payload is used.
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !members.Add(property.Name))
            {
                throw new BridgeRpcException(BridgeRpcException.InvalidRequestCode, "The Bridge sent a duplicate or unknown protocol member.", null)
                {
                    IsInvalidFrame = true,
                };
            }
        }
    }

    private static BridgeRpcException RpcError(JsonObject error, bool invalidFrame = false)
    {
        int code = error["code"]?.GetValueKind() == JsonValueKind.Number ? error["code"]!.GetValue<int>() : 0;
        return new BridgeRpcException(code, error["message"]?.ToString() ?? "error", error["data"]?.DeepClone())
        {
            IsInvalidFrame = invalidFrame,
        };
    }

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

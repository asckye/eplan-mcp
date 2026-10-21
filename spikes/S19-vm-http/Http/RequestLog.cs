using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

namespace EplanMcp.Spikes.S19.Http;

/// <summary>
/// One JSON line per HTTP request, for the README: what a client sends (every header, Origin and User-Agent included),
/// what the gate decided and the final status. The Authorization value is never written: only its scheme, its length
/// and whether it matched. For an authenticated POST it also records the JSON-RPC method and protocol version.
/// </summary>
internal sealed class RequestLog
{
    private const int PeekLimit = 64 * 1024;
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Lock _lock = new();
    private readonly string _file;

    private RequestLog(string file) => _file = file;

    public static RequestLog? Open(string? file) => file is { Length: > 0 } ? new RequestLog(file) : null;

    public void Write(HttpContext context, bool keyMatched, string decision, int status, string? rpc)
    {
        var headers = new JsonObject();
        foreach ((string name, Microsoft.Extensions.Primitives.StringValues values) in context.Request.Headers)
        {
            headers[name] = name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                ? Redacted(values.ToString(), keyMatched)
                : values.ToString();
        }

        var record = new JsonObject
        {
            ["time"] = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["remote"] = context.Connection.RemoteIpAddress?.ToString(),
            ["method"] = context.Request.Method,
            ["path"] = context.Request.Path.Value + context.Request.QueryString.Value,
            ["protocol"] = context.Request.Protocol,
            ["decision"] = decision,
            ["status"] = status,
            ["rpc"] = rpc,
            ["responseSessionId"] = context.Response.Headers["Mcp-Session-Id"].ToString() is { Length: > 0 } id ? id : null,
            ["headers"] = headers,
        };
        string line = record.ToJsonString() + Environment.NewLine;
        lock (_lock)
        {
            File.AppendAllText(_file, line, _utf8);
        }
    }

    /// <summary>"method protocolVersion clientName" of a JSON-RPC POST body (or of each message in a batch); the body stays readable.</summary>
    public static async Task<string?> PeekRpcAsync(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method) || request.ContentLength is > PeekLimit)
        {
            return null;
        }

        request.EnableBuffering(PeekLimit);
        try
        {
            using JsonDocument body = await JsonDocument.ParseAsync(request.Body);
            IEnumerable<JsonElement> messages = body.RootElement.ValueKind == JsonValueKind.Array
                ? body.RootElement.EnumerateArray().ToArray()
                : [body.RootElement];
            return string.Join(" | ", messages.Select(Describe));
        }
        catch (JsonException)
        {
            return "(not JSON)";
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static string Describe(JsonElement message)
    {
        string method = message.TryGetProperty("method", out JsonElement m) ? m.GetString() ?? "?" : "(response)";
        var parts = new List<string> { method };
        if (message.TryGetProperty("params", out JsonElement p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("protocolVersion", out JsonElement v))
            {
                parts.Add("protocolVersion=" + v.GetString());
            }

            if (p.TryGetProperty("clientInfo", out JsonElement c) && c.TryGetProperty("name", out JsonElement n))
            {
                parts.Add("clientInfo=" + n.GetString() + (c.TryGetProperty("version", out JsonElement cv) ? "/" + cv.GetString() : string.Empty));
            }

            if (p.TryGetProperty("name", out JsonElement tool))
            {
                parts.Add("name=" + tool.GetString());
            }

            if (p.TryGetProperty("_meta", out JsonElement meta) && meta.ValueKind == JsonValueKind.Object)
            {
                parts.Add("_meta=[" + string.Join(",", meta.EnumerateObject().Select(o => o.Name)) + "]");
            }
        }

        return string.Join(" ", parts);
    }

    private static string Redacted(string value, bool matched)
    {
        int space = value.IndexOf(' ', StringComparison.Ordinal);
        string scheme = space > 0 ? value[..space] : "(none)";
        return string.Create(CultureInfo.InvariantCulture, $"{scheme} <redacted, {value.Length - space - 1} chars, matched={matched}>");
    }
}

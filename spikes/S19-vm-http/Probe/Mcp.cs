using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace EplanMcp.Spikes.S19.Probe;

/// <summary>The answer to one HTTP request, reduced to what the README reports.</summary>
internal sealed record Reply(int Status, string? SessionId, string ContentType, bool WwwAuthenticate, string? AllowOrigin, string Body, JsonNode? Json)
{
    public string? ErrorCode => Json?["error"]?["code"]?.ToString();

    public string? Text => Json?["result"]?["content"]?[0]?["text"]?.GetValue<string>();
}

/// <summary>
/// Raw Streamable HTTP requests against the prototype, built by hand so that each header is exactly what the check
/// needs. Never uses a proxy; can bind the client socket to another loopback source address (127.0.0.2) to show that
/// the failure limit is per source.
/// </summary>
internal sealed class Mcp : IDisposable
{
    public const string Protocol2025 = "2025-11-25";
    public const string Protocol2026 = "2026-07-28";
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _key;
    private int _id;

    public Mcp(Uri endpoint, string key, IPAddress? source = null)
    {
        _endpoint = endpoint;
        _key = key;
#pragma warning disable CA2000 // the HttpClient below owns and disposes the handler
        var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
#pragma warning restore CA2000
        if (source is not null)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    socket.Bind(new IPEndPoint(source, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    public string Key => _key;

    public static string Initialize(int id, string client = "s19-probe") => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = "initialize",
        ["params"] = new JsonObject
        {
            ["protocolVersion"] = Protocol2025,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = client, ["version"] = "1" },
        },
    }.ToJsonString();

    public string Request(string method, JsonObject? parameters = null) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = ++_id + 100,
        ["method"] = method,
        ["params"] = parameters ?? new JsonObject(),
    }.ToJsonString();

    public static string Notification(string method) => new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }.ToJsonString();

    public string Call(string tool, JsonObject? arguments = null) =>
        Request("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() });

    /// <summary>A 2026-07-28 request: no initialize, client info and capabilities in _meta, the standard headers set by <see cref="PostAsync"/>.</summary>
    public string Request2026(string method, JsonObject? parameters = null)
    {
        JsonObject p = parameters ?? new JsonObject();
        p["_meta"] = new JsonObject
        {
            ["io.modelcontextprotocol/protocolVersion"] = Protocol2026,
            ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "s19-probe-2026", ["version"] = "1" },
            ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
        };
        return Request(method, p);
    }

    /// <summary>
    /// POSTs <paramref name="body"/> to the endpoint (or <paramref name="path"/>) with the key unless
    /// <paramref name="authorization"/> overrides it ("" for none), plus any extra headers.
    /// </summary>
    public Task<Reply> PostAsync(string body, string? sessionId = null, string? protocol = null, string? authorization = null, IDictionary<string, string>? headers = null, string? path = null) =>
        SendAsync(HttpMethod.Post, body, sessionId, protocol, authorization, headers, path);

    public async Task<Reply> SendAsync(HttpMethod method, string? body, string? sessionId = null, string? protocol = null, string? authorization = null, IDictionary<string, string>? headers = null, string? path = null)
    {
        using var request = new HttpRequestMessage(method, path is null ? _endpoint : new Uri(_endpoint, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        string auth = authorization ?? "Bearer " + _key;
        if (auth.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", auth);
        }

        if (sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        }

        if (protocol is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocol);
        }

        if (body is not null && JsonNode.Parse(body) is { } message && message["method"]?.GetValue<string>() is { } rpcMethod && protocol == Protocol2026)
        {
            // 2026-07-28 makes the standard request headers mandatory: the method, and for tools/call the tool's name.
            request.Headers.TryAddWithoutValidation("Mcp-Method", rpcMethod);
            if (message["params"]?["name"]?.GetValue<string>() is { } name)
            {
                request.Headers.TryAddWithoutValidation("Mcp-Name", name);
            }
        }

        foreach ((string name, string value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        string text = await ReadAsync(response, contentType);
        JsonNode? json = null;
        string payload = contentType == "text/event-stream" ? LastData(text) : text;
        if (payload.Length > 0 && (payload[0] == '{' || payload[0] == '['))
        {
            json = JsonNode.Parse(payload);
        }

        return new Reply(
            (int)response.StatusCode,
            response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? ids) ? ids.First() : null,
            contentType,
            response.Headers.WwwAuthenticate.Count > 0,
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? allow) ? allow.First() : null,
            text,
            json);
    }

    public void Dispose() => _http.Dispose();

    private static async Task<string> ReadAsync(HttpResponseMessage response, string contentType)
    {
        if (contentType != "text/event-stream")
        {
            return await response.Content.ReadAsStringAsync();
        }

        // A POST's event stream ends after the response; read lines until the first data event that is a response.
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var text = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            text.AppendLine(line);
            if (line.StartsWith("data:", StringComparison.Ordinal) && line.Contains("\"id\"", StringComparison.Ordinal))
            {
                break;
            }
        }

        return text.ToString();
    }

    private static string LastData(string events) =>
        events.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()).LastOrDefault() ?? string.Empty;
}

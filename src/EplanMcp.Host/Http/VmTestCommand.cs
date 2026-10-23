using System.Globalization;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Http;

/// <summary>ADR-0018 connection test: two requests, no notifications, GET, retries, session cleanup or tool calls.</summary>
internal static class VmTestCommand
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private const string Protocol = "2025-11-25";
    private static readonly JsonDocumentOptions _json = new() { AllowDuplicateProperties = false };

    public static async Task<int> RunAsync(VmTestRequest request, TextWriter output, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false };
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        return await RunAsync(http, request, Console.IsInputRedirected ? Console.In : TextReader.Null, output, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> RunAsync(HttpClient http, VmTestRequest request, TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        VmTestResult result = await TestAsync(http, request.Url, input, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(request.Json ? FormatJson(result) : result.Failure is { } failure ? "VM connection failed: " + failure
            : string.Create(CultureInfo.InvariantCulture, $"Connected: {result.ServerName} {result.ServerVersion}; tools: {result.ToolCount}.")).ConfigureAwait(false);
        return result.Failure is null ? 0 : 1;
    }

    internal static string FormatJson(VmTestResult result) => new JsonObject
    {
        ["schemaVersion"] = 1,
        ["server"] = result.Failure is null ? new JsonObject { ["name"] = result.ServerName, ["version"] = result.ServerVersion } : null,
        ["toolCount"] = result.ToolCount,
        ["failure"] = result.Failure,
    }.ToJsonString(HostJson.Options);

    internal static async Task<VmTestResult> TestAsync(HttpClient http, string url, TextReader input, CancellationToken cancellationToken)
    {
        try
        {
            Require(RemoteServer.ValidUrl(url));
            RemoteServer remote = RemoteServer.Read(url, input);
            var parameters = new JsonObject
            {
                ["protocolVersion"] = Protocol,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "eplanmcp-vm-test", ["version"] = HostInfo.Version },
            };
            using JsonDocument initialized = await SendAsync(http, remote, url, 1, "initialize", parameters, cancellationToken).ConfigureAwait(false);
            JsonElement init = Result(initialized, 1);
            Require(init.GetProperty("protocolVersion").GetString() == Protocol && init.GetProperty("capabilities").ValueKind == JsonValueKind.Object);
            JsonElement server = init.GetProperty("serverInfo");
            string name = Text(server.GetProperty("name")), version = Text(server.GetProperty("version"));
            using JsonDocument listed = await SendAsync(http, remote, url, 2, "tools/list", new JsonObject(), cancellationToken).ConfigureAwait(false);
            JsonElement list = Result(listed, 2), tools = list.GetProperty("tools");
            Require(tools.ValueKind == JsonValueKind.Array && (!list.TryGetProperty("nextCursor", out JsonElement cursor) || cursor.ValueKind == JsonValueKind.Null));
            foreach (JsonElement tool in tools.EnumerateArray())
            {
                _ = Text(tool.GetProperty("name"));
                Require(tool.GetProperty("inputSchema").ValueKind == JsonValueKind.Object);
            }

            return new(name, version, tools.GetArrayLength(), null);
        }
        catch (VmTestHttpException ex)
        {
            return VmTestResult.Failed(ex.Failure);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return VmTestResult.Failed("timed_out");
        }
        catch (Exception ex) when (ex is ClientConfigException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            return VmTestResult.Failed("protocol_error");
        }
        catch (HttpRequestException ex)
        {
            return VmTestResult.Failed(ex.HttpRequestError is HttpRequestError.InvalidResponse or HttpRequestError.HttpProtocolError ? "protocol_error" : "unreachable");
        }
        catch (IOException)
        {
            return VmTestResult.Failed("unreachable");
        }
    }

    private static async Task<JsonDocument> SendAsync(HttpClient http, RemoteServer remote, string url, int id, string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(RequestTimeout); // Includes headers, JSON bodies and an SSE stream that never finishes an event.
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        remote.Authorize(request);
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", Protocol);
        request.Headers.Add("Mcp-Method", method);
        request.Content = new StringContent(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
        string? failure = response.StatusCode switch
        {
            HttpStatusCode.OK => null,
            HttpStatusCode.Unauthorized => "unauthorized",
            HttpStatusCode.Forbidden => "forbidden",
            HttpStatusCode.TooManyRequests => "rate_limited",
            _ => "protocol_error",
        };
        if (failure is not null)
        {
            throw new VmTestHttpException(failure);
        }

        using var body = new BoundedBody(await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false));
        switch (response.Content.Headers.ContentType?.MediaType)
        {
            case "application/json":
                return await JsonDocument.ParseAsync(body, _json, limit.Token).ConfigureAwait(false);
            case "text/event-stream":
                await foreach (SseItem<string> item in SseParser.Create(body).EnumerateAsync(limit.Token).ConfigureAwait(false))
                {
                    Require(item.EventType == "message");
                    return JsonDocument.Parse(item.Data, _json);
                }
                break;
        }

        throw new InvalidDataException("Expected a JSON-RPC response.");
    }

    private static JsonElement Result(JsonDocument document, int id)
    {
        JsonElement root = document.RootElement;
        Require(root.GetProperty("jsonrpc").GetString() == "2.0" && root.GetProperty("id").TryGetInt32(out int actual) && actual == id
            && !root.TryGetProperty("error", out _) && root.GetProperty("result").ValueKind == JsonValueKind.Object);
        return root.GetProperty("result");
    }

    private static string Text(JsonElement value)
    {
        string? text = value.GetString();
        Require(!string.IsNullOrWhiteSpace(text));
        return ApprovalTexts.Value(RemoteServer.Redact(text)!, zh: false);
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidDataException("Invalid MCP response or VM test input.");
        }
    }

    private sealed class VmTestHttpException(string failure) : Exception
    {
        public string Failure { get; } = failure;
    }

    // Bound both the JSON document and the SSE parser's buffer, even when a peer never sends a newline.
    private sealed class BoundedBody(Stream inner) : Stream
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private int _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, MaxBytes - _read + 1)], cancellationToken).ConfigureAwait(false);
            _read += read;
            Require(_read <= MaxBytes);
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

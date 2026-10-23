using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Small")]
public sealed class VmTestCommandTests
{
    private const string Url = "http://192.168.1.42:8766/mcp";
    private static readonly string _key = new('A', 43);
    private const string Initialized = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{"tools":{}},"serverInfo":{"name":"eplan-vm","version":"1.2.3"}}}""";
    private const string Listed = """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"eplan_project_list","inputSchema":{"type":"object"}}]}}""";

    [Theory]
    [InlineData("--url http://192.168.1.42:8766/mcp --key-stdin", false)]
    [InlineData("--key-stdin --format json --url http://192.168.1.42:8766/mcp", true)]
    public void Arguments_RequireAnExplicitIpv4EndpointAndStdin(string options, bool json)
    {
        CliArguments cli = Assert.IsType<CliArguments>(CliArguments.Parse(["vm-test", .. options.Split(' ')]));
        Assert.Equal((CliCommand.VmTest, json, new VmTestRequest(Url, json)), (cli.Command, cli.Json, cli.VmTest));
        Assert.Equal(TimeSpan.FromSeconds(10), VmTestCommand.RequestTimeout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--url http://192.168.1.42:8766/mcp")]
    [InlineData("--key-stdin")]
    [InlineData("--url http://192.168.1.42:8766/mcp --key-stdin --key secret")]
    [InlineData("--url http://192.168.1.42:8766/mcp --key-stdin --key-stdin")]
    [InlineData("--url http://192.168.1.42:8766/mcp --key-stdin --url http://192.168.1.42:8766/mcp")]
    [InlineData("--url http://192.168.1.42:8766/mcp --key-stdin --format text")]
    [InlineData("--url http://192.168.1.42:8766/mcp --key-stdin --format")]
    [InlineData("--url http://192.168.1.42:8766/mcp --key-stdin --format json --format json")]
    [InlineData("--url https://192.168.1.42:8766/mcp --key-stdin")]
    [InlineData("--url http://localhost:8766/mcp --key-stdin")]
    [InlineData("--url http://[::1]:8766/mcp --key-stdin")]
    [InlineData("--url http://192.168.1.42:0/mcp --key-stdin")]
    [InlineData("--url http://192.168.1.42:65536/mcp --key-stdin")]
    [InlineData("--url http://192.168.1.42:8766/mcp?key=secret --key-stdin")]
    [InlineData("--url http://192.168.1.42:8766/mcp/ --key-stdin")]
    [InlineData("--url http://user@192.168.1.42:8766/mcp --key-stdin")]
    [InlineData("--url http://192.168.001.42:8766/mcp --key-stdin")]
    public void Arguments_RejectMissingDuplicateUnknownOrSecretOptions(string options) =>
        Assert.Null(CliArguments.Parse(["vm-test", .. options.Split(' ', StringSplitOptions.RemoveEmptyEntries)]));

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "\n")]
    [InlineData(true, "\r\n")]
    public async Task Success_SendsOnlyInitializeThenList_WithBearerOnBothRequests(bool sse, string newline)
    {
        using var handler = new Handler((_, index) => Reply(index == 1 ? Initialized : Listed, sse));
        using var http = new HttpClient(handler);
        using var input = new StringReader(_key + newline);
        VmTestResult result = await VmTestCommand.TestAsync(http, Url, input, Token);

        Assert.Equal(new VmTestResult("eplan-vm", "1.2.3", 1, null), result);
        Assert.Equal(["initialize", "tools/list"], handler.Requests.Select(r => JsonNode.Parse(r.Body)!["method"]!.GetValue<string>()));
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal((HttpMethod.Post, Url, "Bearer " + _key, "2025-11-25", "application/json", true), (r.Method, r.Url, r.Authorization, r.Protocol, r.ContentType, r.Cancellable));
            Assert.Contains("application/json", r.Accept, StringComparison.Ordinal);
            Assert.Contains("text/event-stream", r.Accept, StringComparison.Ordinal);
            Assert.False(r.HasOrigin);
            Assert.DoesNotContain(_key, r.Body, StringComparison.Ordinal);
        });
        Assert.Equal([1, 2], handler.Requests.Select(r => JsonNode.Parse(r.Body)!["id"]!.GetValue<int>()));
        JsonNode init = JsonNode.Parse(handler.Requests[0].Body)!["params"]!;
        Assert.Equal("2025-11-25", init["protocolVersion"]!.GetValue<string>());
        Assert.Equal("eplanmcp-vm-test", init["clientInfo"]!["name"]!.GetValue<string>());
        Assert.Empty(init["capabilities"]!.AsObject());
        Assert.Empty(JsonNode.Parse(handler.Requests[1].Body)!["params"]!.AsObject());
    }

    [Theory]
    [InlineData(401, "unauthorized", 1)]
    [InlineData(403, "forbidden", 1)]
    [InlineData(429, "rate_limited", 1)]
    [InlineData(401, "unauthorized", 2)]
    [InlineData(403, "forbidden", 2)]
    [InlineData(429, "rate_limited", 2)]
    [InlineData(302, "protocol_error", 1)]
    [InlineData(404, "protocol_error", 1)]
    [InlineData(500, "protocol_error", 2)]
    [InlineData(202, "protocol_error", 1)]
    public async Task HttpFailure_ReportsExactlyOneCode_AndStops(int status, string failure, int step)
    {
        using var handler = new Handler((_, index) => index < step ? Reply(Initialized) : new((HttpStatusCode)status) { Content = new StringContent(_key) });
        await AssertFailure(handler, failure, step);
    }

    [Theory]
    [InlineData("unreachable", 1)]
    [InlineData("unreachable", 2)]
    [InlineData("timed_out", 1)]
    [InlineData("timed_out", 2)]
    [InlineData("protocol_error", 1)]
    public async Task TransportFailure_IsClassifiedWithoutExceptionTextOrRetries(string failure, int step)
    {
        using var handler = new Handler((_, index) => index < step ? Reply(Initialized) : throw (failure switch
        {
            "timed_out" => new TaskCanceledException(_key),
            "protocol_error" => new HttpRequestException(HttpRequestError.HttpProtocolError, _key),
            _ => new HttpRequestException(HttpRequestError.ConnectionError, _key),
        }));
        await AssertFailure(handler, failure, step);
    }

    [Theory]
    [InlineData(1, "{}")]
    [InlineData(1, "not json")]
    [InlineData(1, "[]")]
    [InlineData(1, "null")]
    [InlineData(1, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}")]
    [InlineData(1, "{\"jsonrpc\":\"1.0\",\"id\":1,\"result\":{}}")]
    [InlineData(1, "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32600,\"message\":\"bad\"}}")]
    [InlineData(1, "{\"jsonrpc\":\"2.0\",\"id\":1,\"id\":1,\"result\":{}}")]
    [InlineData(1, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"future\"}}")]
    [InlineData(2, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":null}}")]
    [InlineData(2, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[null]}}")]
    [InlineData(2, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{}]}}")]
    [InlineData(2, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[],\"nextCursor\":\"more\"}}")]
    [InlineData(2, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"x\",\"inputSchema\":false}]}}")]
    public async Task InvalidProtocol_StopsWithNoPartialSuccess(int step, string body)
    {
        using var handler = new Handler((_, index) => Reply(index < step ? Initialized : body));
        await AssertFailure(handler, "protocol_error", step);
    }

    [Theory]
    [InlineData("wrong_protocol")]
    [InlineData("missing_capabilities")]
    [InlineData("missing_server_name")]
    [InlineData("null_server_name")]
    [InlineData("empty_server_version")]
    public async Task InvalidInitialization_NeverListsTools(string problem)
    {
        JsonNode init = JsonNode.Parse(Initialized)!;
        switch (problem)
        {
            case "wrong_protocol":
                init["result"]!["protocolVersion"] = "future";
                break;
            case "missing_capabilities":
                init["result"]!.AsObject().Remove("capabilities");
                break;
            case "missing_server_name":
                init["result"]!["serverInfo"]!.AsObject().Remove("name");
                break;
            case "null_server_name":
                init["result"]!["serverInfo"]!["name"] = null;
                break;
            default:
                init["result"]!["serverInfo"]!["version"] = "";
                break;
        }
        using var handler = new Handler((_, _) => Reply(init.ToJsonString()));
        await AssertFailure(handler, "protocol_error", 1);
    }

    [Theory]
    [InlineData("text/html", "<html>not MCP</html>")]
    [InlineData("text/event-stream", ": heartbeat\n\n")]
    [InlineData("text/event-stream", "event: endpoint\ndata: /other\n\n")]
    [InlineData("text/event-stream", "event: message\ndata: not json\n\n")]
    public async Task InvalidMediaTypeOrSse_IsAProtocolError(string mediaType, string body)
    {
        using var handler = new Handler((_, _) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        await AssertFailure(handler, "protocol_error", 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutReadingResponseBody_IsTimedOut(bool sse)
    {
        using var handler = new Handler((_, _) =>
        {
            var content = new StreamContent(new EventStream([], timedOut: true));
            content.Headers.ContentType = new(sse ? "text/event-stream" : "application/json");
            return new(HttpStatusCode.OK) { Content = content };
        });
        await AssertFailure(handler, "timed_out", 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcessiveResponse_IsRejected(bool sse)
    {
        using var handler = new Handler((_, _) => Reply(new string('x', 8 * 1024 * 1024 + 1), sse));
        await AssertFailure(handler, "protocol_error", 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    [InlineData("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    public async Task InvalidStdinKey_UsesClientConfigValidationAndNeverSends(string input)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Must not send."));
        using var http = new HttpClient(handler);
        using var reader = new StringReader(input);
        VmTestResult result = await VmTestCommand.TestAsync(http, Url, reader, Token);
        Assert.Equal(VmTestResult.Failed("protocol_error"), result);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Output_ReportsServerAndCount_RedactsEchoedKeyAndControlCharacters(bool json)
    {
        JsonNode init = JsonNode.Parse(Initialized)!;
        init["result"]!["serverInfo"]!["name"] = "server\n\u001b\u202e" + _key;
        using var handler = new Handler((_, index) => Reply(index == 1 ? init.ToJsonString() : Listed));
        using var http = new HttpClient(handler);
        using var input = new StringReader(_key);
        using var output = new StringWriter();
        Assert.Equal(0, await VmTestCommand.RunAsync(http, new(Url, json), input, output, Token));
        Assert.DoesNotContain(_key, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("<key>", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', output.ToString());
        Assert.DoesNotContain('\u202e', output.ToString());
        if (json)
        {
            JsonNode report = JsonNode.Parse(output.ToString())!;
            Assert.Equal(1, report["schemaVersion"]!.GetValue<int>());
            Assert.Equal(1, report["toolCount"]!.GetValue<int>());
            Assert.Null(report["failure"]);
        }
        else
        {
            Assert.Equal("Connected: server  <key> 1.2.3; tools: 1." + Environment.NewLine, output.ToString());
        }
    }

    [Fact]
    public async Task EmptyTools_IsAValidSuccess()
    {
        using var handler = new Handler((_, index) => Reply(index == 1 ? Initialized : """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}"""));
        using var http = new HttpClient(handler);
        using var input = new StringReader(_key);
        Assert.Equal(new VmTestResult("eplan-vm", "1.2.3", 0, null), await VmTestCommand.TestAsync(http, Url, input, Token));
    }

    private static async Task AssertFailure(Handler handler, string failure, int requests)
    {
        using var http = new HttpClient(handler);
        using var input = new StringReader(_key);
        using var output = new StringWriter();
        Assert.Equal(1, await VmTestCommand.RunAsync(http, new(Url, true), input, output, Token));
        Assert.Equal(requests, handler.Requests.Count);
        JsonNode report = JsonNode.Parse(output.ToString())!;
        Assert.Equal(failure, report["failure"]!.GetValue<string>());
        Assert.Null(report["server"]);
        Assert.Null(report["toolCount"]);
        Assert.DoesNotContain(_key, output.ToString(), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Reply(string json, bool sse = false)
    {
        HttpContent content = sse ? new StreamContent(new EventStream(Encoding.UTF8.GetBytes(": comment\r\n\r\nevent: message\r\ndata: " + json + "\r\n\r\n"))) : new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new(sse ? "text/event-stream" : "application/json");
        return new(HttpStatusCode.OK) { Content = content };
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Sent(HttpMethod Method, string Url, string Authorization, string Protocol, string Accept, string? ContentType, bool HasOrigin, bool Cancellable, string Body);

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization!.ToString(), request.Headers.GetValues("MCP-Protocol-Version").Single(),
                request.Headers.Accept.ToString(), request.Content?.Headers.ContentType?.MediaType, request.Headers.Contains("Origin"), cancellationToken.CanBeCanceled,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return response(request, Requests.Count);
        }
    }

    // Fail on reads after the event: a connection test must finish without waiting for the SSE server to close.
    private sealed class EventStream(byte[] data, bool timedOut = false) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => timedOut
            ? ValueTask.FromException<int>(new TaskCanceledException(_key)) : Position == Length
            ? ValueTask.FromException<int>(new InvalidOperationException("SSE remains open.")) : base.ReadAsync(buffer, cancellationToken);
    }
}

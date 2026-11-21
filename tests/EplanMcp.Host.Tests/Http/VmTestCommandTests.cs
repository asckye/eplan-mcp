using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Small")]
public sealed class VmTestCommandTests
{
    private const string Url = "http://192.168.1.42:8766/mcp";
    private const string Initialized = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{"tools":{}},"serverInfo":{"name":"eplan-vm","version":"1.2.3"}}}""";
    private const string Listed = """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"eplan_project_list","inputSchema":{"type":"object"}}]}}""";

    [Theory]
    [InlineData("--url http://192.168.1.42:8766/mcp", false)]
    [InlineData("--format json --url http://192.168.1.42:8766/mcp", true)]
    public void Arguments_RequireAnExplicitIpv4Endpoint(string options, bool json)
    {
        CliArguments cli = Assert.IsType<CliArguments>(CliArguments.Parse(["vm-test", .. options.Split(' ')]));
        Assert.Equal((CliCommand.VmTest, json, new VmTestRequest(Url, json)), (cli.Command, cli.Json, cli.VmTest));
        Assert.Equal(TimeSpan.FromSeconds(10), VmTestCommand.RequestTimeout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--key-stdin")]
    [InlineData("--url http://192.168.1.42:8766/mcp --key secret")]
    [InlineData("--url http://192.168.1.42:8766/mcp --url http://192.168.1.42:8766/mcp")]
    [InlineData("--url http://192.168.1.42:8766/mcp --format text")]
    [InlineData("--url https://192.168.1.42:8766/mcp")]
    [InlineData("--url http://localhost:8766/mcp")]
    [InlineData("--url http://[::1]:8766/mcp")]
    [InlineData("--url http://192.168.1.42:0/mcp")]
    [InlineData("--url http://192.168.1.42:65536/mcp")]
    [InlineData("--url http://192.168.1.42:8766/mcp?key=secret")]
    public void Arguments_RejectMissingDuplicateOrInvalidOptions(string options) =>
        Assert.Null(CliArguments.Parse(["vm-test", .. options.Split(' ', StringSplitOptions.RemoveEmptyEntries)]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Success_SendsOnlyInitializeThenList_WithoutAuthorizationHeader(bool sse)
    {
        using var handler = new Handler((_, index) => Reply(index == 1 ? Initialized : Listed, sse));
        using var http = new HttpClient(handler);
        VmTestResult result = await VmTestCommand.TestAsync(http, Url, TextReader.Null, Token);

        Assert.Equal(new VmTestResult("eplan-vm", "1.2.3", 1, null), result);
        Assert.Equal(["initialize", "tools/list"], handler.Requests.Select(r => JsonNode.Parse(r.Body)!["method"]!.GetValue<string>()));
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal((HttpMethod.Post, Url, (string?)null, "2025-11-25", "application/json", true), (r.Method, r.Url, r.Authorization, r.Protocol, r.ContentType, r.Cancellable));
            Assert.False(r.HasOrigin);
        });
    }

    [Theory]
    [InlineData(403, "forbidden", 1)]
    [InlineData(404, "protocol_error", 1)]
    [InlineData(500, "protocol_error", 2)]
    public async Task HttpFailure_ReportsExactlyOneCode_AndStops(int status, string failure, int step)
    {
        using var handler = new Handler((_, index) => index < step ? Reply(Initialized) : new((HttpStatusCode)status));
        using var http = new HttpClient(handler);
        using var output = new StringWriter();
        Assert.Equal(1, await VmTestCommand.RunAsync(http, new(Url, true), TextReader.Null, output, Token));
        Assert.Equal(step, handler.Requests.Count);
        JsonNode report = JsonNode.Parse(output.ToString())!;
        Assert.Equal(failure, report["failure"]!.GetValue<string>());
    }

    [Fact]
    public async Task EmptyTools_IsAValidSuccess()
    {
        using var handler = new Handler((_, index) => Reply(index == 1 ? Initialized : """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}"""));
        using var http = new HttpClient(handler);
        Assert.Equal(new VmTestResult("eplan-vm", "1.2.3", 0, null), await VmTestCommand.TestAsync(http, Url, TextReader.Null, Token));
    }

    private static HttpResponseMessage Reply(string json, bool sse = false)
    {
        HttpContent content = sse ? new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(": comment\r\n\r\nevent: message\r\ndata: " + json + "\r\n\r\n"))) : new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new(sse ? "text/event-stream" : "application/json");
        return new(HttpStatusCode.OK) { Content = content };
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Sent(HttpMethod Method, string Url, string? Authorization, string Protocol, string Accept, string? ContentType, bool HasOrigin, bool Cancellable, string Body);

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), request.Headers.GetValues("MCP-Protocol-Version").Single(),
                request.Headers.Accept.ToString(), request.Content?.Headers.ContentType?.MediaType, request.Headers.Contains("Origin"), cancellationToken.CanBeCanceled,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return response(request, Requests.Count);
        }
    }
}

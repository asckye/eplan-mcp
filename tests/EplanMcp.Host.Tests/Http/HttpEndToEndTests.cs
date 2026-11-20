using System.IO.Pipes;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Http;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Bridge;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace EplanMcp.Host.Tests.Http;

/// <summary>Real Kestrel, MCP SDK and Bridge.Core pipe server; key-free VM HTTP gated by Host/Origin checks and firewall.</summary>
[Trait("Size", "Medium")]
public sealed class HttpEndToEndTests : IAsyncLifetime
{
    private readonly HostDataLocations _locations = new(Path.Combine(Path.GetTempPath(), "EplanMcpHttpTests-" + Guid.NewGuid().ToString("N")));
    private readonly FakeDiscovery _discovery = new();
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private WebApplication? _app;
    private BridgeServer? _bridge;

    public async ValueTask InitializeAsync()
    {
        string pipe = "EplanMcpHttpTest." + Guid.NewGuid().ToString("N");
        var router = new OperationRouter([new ProjectListOperation(new Catalog()), new ContextTestSnapshot()], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(2));
        _bridge = new BridgeServer(pipe,
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "http-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3" } },
            router, new NullLog());
        _bridge.Start();
        _discovery.Endpoints.Add(new BridgeEndpoint(pipe, Environment.ProcessId));
        _discovery.Endpoints.Add(new BridgeEndpoint("other-instance", 42));
        _app = VmHttp.Build(new HttpArguments(IPAddress.Loopback, 0, Environment.ProcessId), _locations,
            HostConfig.Defaults(_locations) with { HttpAllowedOrigins = ["http://trusted.example"] }, _discovery);
        await _app.StartAsync(Token);
        _http.BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        Assert.Equal("127.0.0.1", _http.BaseAddress.Host);
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync(Token);
            await _app.DisposeAsync();
        }

        _bridge?.Dispose();
        if (Directory.Exists(_locations.Root))
        {
            Directory.Delete(_locations.Root, recursive: true);
        }
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOkWithoutKey()
    {
        using HttpResponseMessage response = await _http.GetAsync("/health", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(Token);
        Assert.Contains("\"ok\":true", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ContextGet_ReadWorksOnStatelessHttp_WithBothJsonCopiesAndNoExecution(string protocol)
    {
        JsonNode response = await RpcAsync("tools/call", new JsonObject { ["name"] = "eplan_context_get", ["arguments"] = new JsonObject { ["scope"] = new JsonObject(), ["max_tokens"] = 20000 } }, protocol);
        JsonNode call = response["result"]!;
        Assert.False(call["isError"]?.GetValue<bool>() ?? false, response.ToJsonString());
        JsonNode data = call["structuredContent"]!;
        Assert.Equal("Line1", data["project"]!["name"]!.GetValue<string>());
        Assert.Equal(4, data["objects"]!.AsArray().Count);
        Assert.True(JsonNode.DeepEquals(data, JsonNode.Parse(call["content"]![1]!["text"]!.GetValue<string>())));
        Assert.Null(data["execution"]);
        Assert.False(Directory.Exists(_locations.Workspaces));
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task KeyFreeHttp_ListsToolsReadsBridgeAndAuditsSource_WithoutSessions(string protocol)
    {
        JsonNode list = await RpcAsync("tools/list", new JsonObject(), protocol);
        Assert.Contains(list["result"]!["tools"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "eplan_project_list");
        Assert.Contains(list["result"]!["tools"]!.AsArray(), t => t!["name"]!.GetValue<string>() == PlanTools.GetTool);
        JsonNode read = await RpcAsync("tools/call", new JsonObject { ["name"] = "eplan_project_list", ["arguments"] = new JsonObject() }, protocol);
        Assert.False(read["result"]?["isError"]?.GetValue<bool>() ?? false, read.ToJsonString());
        Assert.Equal("Line1", read["result"]!["structuredContent"]!["projects"]![0]!["name"]!.GetValue<string>());
        string[] audit = Directory.GetFiles(_locations.Audit, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();
        Assert.Equal(2, audit.Length);
        Assert.All(audit, line => Assert.Equal("127.0.0.1", JsonNode.Parse(line)!["sourceAddress"]!.GetValue<string>()));
        Assert.Null(AuditVerifier.Verify(_locations.Audit).Break);
    }

    [Fact]
    public async Task VmTest_RealLoopbackTransport_InitializesAndListsKeyFree()
    {
        using var output = new StringWriter();
        int exit = await VmTestCommand.RunAsync(_http, new(new Uri(_http.BaseAddress!, "/mcp").AbsoluteUri, true), TextReader.Null, output, Token);
        JsonNode report = JsonNode.Parse(output.ToString())!;
        Assert.Equal(0, exit);
        Assert.NotEmpty(report["server"]!["name"]!.GetValue<string>());
        Assert.Equal(HostInfo.Version, report["server"]!["version"]!.GetValue<string>());
        Assert.True(report["toolCount"]!.GetValue<int>() > 0);
        Assert.Null(report["failure"]);
    }

    [Theory]
    [InlineData("http://foreign.example", "/mcp", 403)]
    [InlineData("null", "/mcp", 403)]
    [InlineData(null, "/mcp/health", 404)]
    [InlineData(null, "/sse", 404)]
    [InlineData(null, "/message", 404)]
    public async Task EveryPath_EnforcesOriginAndKnownRoutes(string? origin, string path, int expected)
    {
        using HttpResponseMessage response = await SendAsync("{}", origin, path);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Token));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("attacker.example")]
    [InlineData("localhost")]
    public async Task ForeignHostHeader_IsRefused(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Host = $"{host}:{_http.BaseAddress!.Port}";

        using HttpResponseMessage response = await _http.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task AllowedOrigin_SucceedsKeyFree()
    {
        await RpcAsync("tools/list", new JsonObject(), "2025-11-25", "http://trusted.example");
    }

    [Fact]
    public async Task FixedInstance_SelectOnlyReportsIt_AndNeverFollowsAReplacement()
    {
        JsonNode selected = await RpcAsync("tools/call", new JsonObject { ["name"] = "eplan_session_select", ["arguments"] = new JsonObject { ["process_id"] = 42 } }, "2025-11-25");
        Assert.False(selected["result"]?["isError"]?.GetValue<bool>() ?? false, selected.ToJsonString());
        Assert.Equal(Environment.ProcessId, selected["result"]!["structuredContent"]!["selected"]!["processId"]!.GetValue<int>());
        _discovery.Endpoints[0] = new BridgeEndpoint("restarted-pipe", Environment.ProcessId);
        JsonNode read = await RpcAsync("tools/call", new JsonObject { ["name"] = "eplan_project_list", ["arguments"] = new JsonObject() }, "2025-11-25");
        Assert.True(read["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains(ErrorReason.EplanNotConnected, read.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_RefusesMissingOrAmbiguousSelection()
    {
        Assert.Throws<HttpStartException>(() => VmHttp.Build(new HttpArguments(IPAddress.Loopback, 0, null), _locations, HostConfig.Defaults(_locations), _discovery));
        Assert.Throws<HttpStartException>(() => VmHttp.Build(new HttpArguments(IPAddress.Loopback, 0, 999), _locations, HostConfig.Defaults(_locations), _discovery));
    }

    private async Task<JsonNode> RpcAsync(string method, JsonObject parameters, string protocol, string? origin = null)
    {
        if (protocol == "2026-07-28")
        {
            parameters["_meta"] = new JsonObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = protocol,
                ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "http-tests", ["version"] = "1" },
                ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
            };
        }

        string body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters }.ToJsonString();
        using HttpResponseMessage response = await SendAsync(body, origin, protocol: protocol, method: method, name: (parameters["name"] ?? parameters["uri"])?.GetValue<string>());
        string text = await response.Content.ReadAsStringAsync(Token);
        Assert.True(response.IsSuccessStatusCode, text);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
        string json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Last(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..] : text;
        JsonNode result = JsonNode.Parse(json)!;
        Assert.True(result["error"] is null, json);
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(string body, string? origin = null, string path = "/mcp", string protocol = "2025-11-25", string? method = null, string? name = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("Mcp-Protocol-Version", protocol);

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (method is not null)
        {
            request.Headers.Add("Mcp-Method", method);
        }

        if (name is not null)
        {
            request.Headers.Add("Mcp-Name", name);
        }

        return await _http.SendAsync(request, Token);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = "Line1", Path = @"D:\Fake\Line1", Current = true }];
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}

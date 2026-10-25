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

/// <summary>Real Kestrel, MCP SDK and Bridge.Core pipe server; only the EPLAN adapters are fake.</summary>
[Trait("Size", "Medium")]
public sealed class HttpEndToEndTests : IAsyncLifetime
{
    private readonly HostDataLocations _locations = new(Path.Combine(Path.GetTempPath(), "EplanMcpHttpTests-" + Guid.NewGuid().ToString("N")));
    private readonly FakeDiscovery _discovery = new();
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4) };
    private WebApplication? _app;
    private BridgeServer? _bridge;
    private string _key = string.Empty;

    public async ValueTask InitializeAsync()
    {
        Assert.Equal(0, await VmKey.RunAsync("create", _locations, TextWriter.Null, Token));
        _key = VmKey.Read(_locations);
        string pipe = "EplanMcpHttpTest." + Guid.NewGuid().ToString("N");
        var router = new OperationRouter([new ProjectListOperation(new Catalog()), new ContextTestSnapshot()], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(2));
        _bridge = new BridgeServer(pipe,
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "http-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3" } },
            router, new NullLog());
        _bridge.Start();
        _discovery.Endpoints.Add(new BridgeEndpoint(pipe, Environment.ProcessId));
        _discovery.Endpoints.Add(new BridgeEndpoint("other-instance", 42));
        // Port 0 is test-only: Kestrel allocates a free port; the CLI refuses it.
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
    public async Task CorrectKey_ListsToolsReadsBridgeAndAuditsSource_WithoutSessions(string protocol)
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
        Assert.DoesNotContain(_key, string.Join('\n', audit), StringComparison.Ordinal);
        Assert.Null(AuditVerifier.Verify(_locations.Audit).Break);
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task PlanGetAndResource_StatelessHttpRefusesStableSessionCapability(string protocol)
    {
        string id = new('a', 32);
        JsonNode tool = await RpcAsync("tools/call", new JsonObject
        {
            ["name"] = PlanTools.GetTool,
            ["arguments"] = new JsonObject { ["plan_id"] = id },
        }, protocol);
        Assert.True(tool["result"]!["isError"]!.GetValue<bool>());
        JsonNode error = tool["result"]!["structuredContent"]!["error"]!;
        Assert.Equal(ErrorReason.CapabilityMissing, error["reason"]!.GetValue<string>());
        Assert.Equal("transport:stable_session", error["metadata"]!["missing"]![0]!.GetValue<string>());
        Assert.Null(tool["result"]!["structuredContent"]!["execution"]);
        JsonNode resource = await RpcAsync("resources/read", new JsonObject { ["uri"] = "eplanmcp://plans/" + id }, protocol);
        JsonNode view = JsonNode.Parse(resource["result"]!["contents"]![0]!["text"]!.GetValue<string>())!;
        Assert.Equal(ErrorReason.CapabilityMissing, view["error"]!["reason"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(_locations.State, "plans")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VmTest_RealLoopbackTransport_InitializesAndListsWithoutCallingTools(bool correctKey)
    {
        using var input = new StringReader(correctKey ? _key : new string('A', 43));
        using var output = new StringWriter();
        int exit = await VmTestCommand.RunAsync(_http, new(new Uri(_http.BaseAddress!, "/mcp").AbsoluteUri, true), input, output, Token);
        JsonNode report = JsonNode.Parse(output.ToString())!;
        Assert.Equal(correctKey ? 0 : 1, exit);
        if (correctKey)
        {
            Assert.NotEmpty(report["server"]!["name"]!.GetValue<string>());
            Assert.Equal(HostInfo.Version, report["server"]!["version"]!.GetValue<string>());
            Assert.True(report["toolCount"]!.GetValue<int>() > 0);
            Assert.Null(report["failure"]);
        }
        else
        {
            Assert.Equal("unauthorized", report["failure"]!.GetValue<string>());
            Assert.Null(report["server"]);
        }

        string[] audit = Directory.GetFiles(_locations.Audit, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();
        Assert.Equal(correctKey ? 2 : 1, audit.Length);
        Assert.All(audit, line => Assert.Equal("http_request", JsonNode.Parse(line)!["kind"]!.GetValue<string>()));
        Assert.DoesNotContain(_key, output.ToString() + string.Join('\n', audit), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, "/mcp", 401)]
    [InlineData("wrong", null, "/mcp", 401)]
    [InlineData(null, null, "/", 401)]
    [InlineData(null, null, "/mcp/health", 401)]
    [InlineData(null, "http://foreign.example", "/mcp", 403)]
    [InlineData("correct", "http://foreign.example", "/mcp", 403)]
    [InlineData("correct", "null", "/mcp", 403)]
    [InlineData("correct", null, "/mcp/health", 404)]
    [InlineData("correct", null, "/sse", 404)]
    [InlineData("correct", null, "/message", 404)]
    public async Task EveryPath_EnforcesAuthenticationAndOrigin(string? key, string? origin, string path, int expected)
    {
        using HttpResponseMessage response = await SendAsync("{}", key == "correct" ? _key : key, origin, path);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Token));
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task AllowedOrigin_StillRequiresTheKey()
    {
        using HttpResponseMessage denied = await SendAsync("{}", null, "http://trusted.example");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await RpcAsync("tools/list", new JsonObject(), "2025-11-25", "http://trusted.example");
    }

    [Fact]
    public async Task TenFailures_BlockEvenTheCorrectKey()
    {
        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage failure = await SendAsync("{}", "wrong");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using HttpResponseMessage locked = await SendAsync("{}", _key);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(string.Empty, await locked.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task AuditUnavailable_PreservesGenericRefusalsAndPreventsToolsRunning()
    {
        File.WriteAllText(_locations.Audit, "blocks directory creation");
        using HttpResponseMessage wrong = await SendAsync("{}", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(string.Empty, await wrong.Content.ReadAsStringAsync(Token));
        using HttpResponseMessage origin = await SendAsync("{}", _key, "http://foreign.example");
        Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
        using HttpResponseMessage right = await SendAsync("{}", _key);
        Assert.Equal(HttpStatusCode.InternalServerError, right.StatusCode);
    }

    [Fact]
    public async Task RotationAndDeletion_InvalidateThePreviousKeyOnTheNextRequest()
    {
        Assert.Equal(0, await VmKey.RunAsync("create", _locations, TextWriter.Null, Token));
        using HttpResponseMessage old = await SendAsync("{}", _key);
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        _key = VmKey.Read(_locations);
        await RpcAsync("tools/list", new JsonObject(), "2025-11-25");
        Assert.Equal(0, await VmKey.RunAsync("delete", _locations, TextWriter.Null, Token));
        using HttpResponseMessage deleted = await SendAsync("{}", _key);
        Assert.Equal(HttpStatusCode.Unauthorized, deleted.StatusCode);
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

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task RequestWithoutBridgeIo_AuditCarriesSourceAndKeepsHashChain(string protocol)
    {
        await RpcAsync("tools/list", new JsonObject(), protocol);
        JsonNode read = await RpcAsync("tools/call", new JsonObject { ["name"] = "eplan_tool_usage", ["arguments"] = new JsonObject { ["tool"] = "eplan_project_list" } }, protocol);
        Assert.False(read["result"]?["isError"]?.GetValue<bool>() ?? false, read.ToJsonString());
        string[] lines = Directory.GetFiles(_locations.Audit, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal("127.0.0.1", JsonNode.Parse(line)!["sourceAddress"]!.GetValue<string>()));
        Assert.DoesNotContain(_key, string.Join('\n', lines), StringComparison.Ordinal);
        Assert.Null(AuditVerifier.Verify(_locations.Audit).Break);
    }

    [Fact]
    public void Discovery_ExposesOnlyThePinnedPipeAndNeverItsReplacement()
    {
        IBridgeDiscovery pinned = _app!.Services.GetRequiredService<IBridgeDiscovery>();
        Assert.Equal(_discovery.Endpoints[0], Assert.Single(pinned.Discover()));
        _discovery.Endpoints[0] = new BridgeEndpoint("replacement", Environment.ProcessId);
        Assert.Empty(pinned.Discover());
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
        using HttpResponseMessage response = await SendAsync(body, _key, origin, protocol: protocol, method: method, name: (parameters["name"] ?? parameters["uri"])?.GetValue<string>());
        string text = await response.Content.ReadAsStringAsync(Token);
        Assert.True(response.IsSuccessStatusCode, text);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
        string json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').Last(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..] : text;
        JsonNode result = JsonNode.Parse(json)!;
        Assert.True(result["error"] is null, json);
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(string body, string? key, string? origin = null, string path = "/mcp", string protocol = "2025-11-25", string? method = null, string? name = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("Mcp-Protocol-Version", protocol);
        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

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

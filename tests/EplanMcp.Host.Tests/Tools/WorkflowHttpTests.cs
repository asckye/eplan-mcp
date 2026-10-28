using System.Net;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Http;
using EplanMcp.Host.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class WorkflowHttpTests
{
    [Theory]
    [InlineData("2025-11-25", WorkflowTools.PlanTool)]
    [InlineData("2026-07-28", WorkflowTools.PlanTool)]
    [InlineData("2025-11-25", PlanApplyTools.ApplyTool)]
    [InlineData("2026-07-28", PlanApplyTools.ApplyTool)]
    [InlineData("2025-11-25", PlanApplyTools.TaskTool)]
    [InlineData("2026-07-28", PlanApplyTools.TaskTool)]
    [InlineData("2025-11-25", RuleTools.CheckTool)]
    [InlineData("2026-07-28", RuleTools.CheckTool)]
    public async Task StatelessHttp_WorkflowTools_RefuseBeforeAnyBridgeReadOrPublication(string protocol, string tool)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("eplanmcp-workflow-http-").FullName;
        var bridge = new FakeBridgeClient();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(new HostDataLocations(root));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<HttpCaller>();
        builder.Services.AddEplanMcp().WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless);
        builder.Services.AddSingleton<IBridgeClient>(bridge);
        await using WebApplication app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(token);
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var http = new HttpClient(handler) { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            string metadata = protocol == "2026-07-28" ? ",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"workflow-test\",\"version\":\"1\"},\"io.modelcontextprotocol/clientCapabilities\":{}}" : string.Empty;
            string args = JsonSerializer.Serialize(tool == RuleTools.CheckTool ? new Dictionary<string, object?> { ["project"] = "Line1", ["scope"] = new { }, ["fix_plan"] = true } : tool == WorkflowTools.PlanTool ? WorkflowProtocolTests.Request()
                : new Dictionary<string, object?> { [tool == PlanApplyTools.TaskTool ? "task_id" : "plan_id"] = "untrusted-id" });
            request.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + args + metadata + "}}", Encoding.UTF8, "application/json");
            request.Headers.Add("Mcp-Protocol-Version", protocol);
            request.Headers.Add("Mcp-Method", "tools/call");
            request.Headers.Add("Mcp-Name", tool);
            request.Headers.Add("Accept", "application/json, text/event-stream");
            using HttpResponseMessage response = await http.SendAsync(request, token);
            string body = await response.Content.ReadAsStringAsync(token);
            Assert.True(response.IsSuccessStatusCode, body);
            string json = response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? body.Split('\n').Last(l => l.StartsWith("data: ", StringComparison.Ordinal))[6..] : body;
            JsonElement result = ContextToolTests.Parse(json).GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean(), json);
            JsonElement error = result.GetProperty("structuredContent").GetProperty("error");
            Assert.Equal(ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
            Assert.Equal("transport:stable_session", Assert.Single(error.GetProperty("metadata").GetProperty("missing").EnumerateArray()).GetString());
            Assert.Empty(bridge.Calls);
            Assert.Empty(Directory.GetFiles(root, "*.json", SearchOption.AllDirectories));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }
}

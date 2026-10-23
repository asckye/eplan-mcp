using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>Real MCP SDK with fake Bridge, without OS pipes; HTTP is stateless as in VmHttp.</summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ContextProtocolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-context-protocol-").FullName);
    private readonly FakeBridgeClient _bridge = new() { Status = new([], new() { Operations = [OperationNames.ProjectSnapshot] }, null) };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ContextProtocolTests() => _bridge.Handlers[OperationNames.ProjectSnapshot] = args => ContextTestSnapshot.Data((ProjectSnapshotArgs)args!);

    [Fact]
    public async Task McpToolCall_DefaultInputSchemaAnnotationsAndReadResultShape()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations);
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: Token);
            McpClientTool tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), t => t.Name == ContextTools.GetTool);
            Assert.Equal("Get engineering context", tool.ProtocolTool.Title);
            Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
            Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
            Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
            Assert.False(tool.ProtocolTool.Annotations.OpenWorldHint);
            JsonElement properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
            Assert.Equal(["project", "cabinet", "scope", "include", "max_tokens"], properties.EnumerateObject().Select(p => p.Name));
            Assert.Equal(5000, properties.GetProperty("max_tokens").GetProperty("default").GetInt32());
            CallToolResult call = await client.CallToolAsync(ContextTools.GetTool, new Dictionary<string, object?> { ["scope"] = new { } }, cancellationToken: Token);
            JsonElement data = ContextToolTests.Data(call);
            Assert.False(data.TryGetProperty("rules", out _));
            Assert.Equal(5000, data.GetProperty("budget").GetProperty("requestedTokens").GetInt32());
            AssertShape(call);
            CallToolResult invalid = await client.CallToolAsync(ContextTools.GetTool, new Dictionary<string, object?> { ["scope"] = new { depth = 2 }, ["include"] = new List<string> { "objects", "objects" } }, cancellationToken: Token);
            string message = ContextToolTests.Error(invalid).GetProperty("metadata").GetProperty("violations").GetString()!;
            Assert.Contains("/scope/depth", message, StringComparison.Ordinal);
            Assert.Contains("/include", message, StringComparison.Ordinal);
            Assert.Single(_bridge.Calls);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task StatelessHttp_ReadNeedsNoStableSession(string protocol)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(_locations);
        builder.Services.AddEplanMcp().WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless);
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        await using WebApplication app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(Token);
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var http = new HttpClient(handler) { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            string metadata = protocol == "2026-07-28" ? ",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"context-test\",\"version\":\"1\"},\"io.modelcontextprotocol/clientCapabilities\":{}}" : string.Empty;
            request.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"eplan_context_get\",\"arguments\":{\"scope\":{}}" + metadata + "}}", Encoding.UTF8, "application/json");
            request.Headers.Add("Mcp-Protocol-Version", protocol);
            request.Headers.Add("Mcp-Method", "tools/call");
            request.Headers.Add("Mcp-Name", ContextTools.GetTool);
            request.Headers.Add("Accept", "application/json, text/event-stream");
            using HttpResponseMessage response = await http.SendAsync(request, Token);
            string body = await response.Content.ReadAsStringAsync(Token);
            Assert.True(response.IsSuccessStatusCode, body);
            Assert.False(response.Headers.Contains("Mcp-Session-Id"));
            string json = response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? body.Split('\n').Last(l => l.StartsWith("data: ", StringComparison.Ordinal))[6..] : body;
            JsonElement result = ContextToolTests.Parse(json).GetProperty("result");
            Assert.False(result.TryGetProperty("isError", out JsonElement error) && error.GetBoolean(), json);
            Assert.Equal("Line1", result.GetProperty("structuredContent").GetProperty("project").GetProperty("name").GetString());
            Assert.False(result.GetProperty("structuredContent").TryGetProperty("execution", out _));
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }

    private static void AssertShape(CallToolResult call)
    {
        JsonElement data = ContextToolTests.Data(call);
        Assert.True(JsonElement.DeepEquals(data, ContextToolTests.Parse(((TextContentBlock)call.Content[1]).Text)));
        Assert.Equal(2, call.Content.Count);
        Assert.False(data.TryGetProperty("execution", out _));
        ContextToolTests.AssertSources(data);
    }
    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}

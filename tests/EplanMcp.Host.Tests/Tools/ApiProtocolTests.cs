using System.IO.Pipelines;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ApiProtocolTests
{
    [Fact]
    public async Task FullProfile_ReadToolsWorkWithoutBridgeAndRejectUnknownArguments()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var bridge = new FakeBridgeClient();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(bridge);
        using IHost host = builder.Build();
        CancellationToken token = TestContext.Current.CancellationToken;
        await host.StartAsync(token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: token);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            foreach (string name in new[] { ApiTools.SearchTool, ApiTools.GetTool })
            {
                var tool = Assert.Single(tools, t => t.Name == name).ProtocolTool;
                Assert.Equal((true, false, true, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint,
                    tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
                Assert.NotNull(tool.OutputSchema);
                Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
            }
            var data = ApiToolTests.Data(await client.CallToolAsync(ApiTools.SearchTool,
                new Dictionary<string, object?> { ["kind"] = "data_dictionary", ["member_name"] = "Function", ["limit"] = 1 }, cancellationToken: token));
            Assert.True(data.GetProperty("total").GetInt32() > 0);
            var details = ApiToolTests.Data(await client.CallToolAsync(ApiTools.GetTool,
                new Dictionary<string, object?> { ["id"] = "D:Function", ["limit"] = 1 }, cancellationToken: token));
            Assert.Equal("dictionary", details.GetProperty("items")[0].GetProperty("kind").GetString());
            ApiToolTests.AssertError(await client.CallToolAsync(ApiTools.GetTool,
                new Dictionary<string, object?> { ["id"] = "D:Function", ["misspelled"] = true }, cancellationToken: token));
            ApiToolTests.AssertError(await client.CallToolAsync(ApiTools.GetTool,
                new Dictionary<string, object?> { ["id"] = "missing" }, cancellationToken: token), ErrorReason.ResourceNotFound);
            Assert.Empty(bridge.Calls);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void CoreProfile_ExcludesBothApiToolsAndTheirUsageNames()
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.ClearProviders());
        services.AddSingleton(new ToolProfileChoice(ToolProfile.Core));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Assert.DoesNotContain(options.ToolCollection!, t => t.ProtocolTool.Name is ApiTools.SearchTool or ApiTools.GetTool);
        var names = options.ToolCollection!.Single(t => t.ProtocolTool.Name == UsageTools.UsageTool).ProtocolTool.InputSchema
            .GetProperty("properties").GetProperty("tool").GetProperty("enum").EnumerateArray().Select(n => n.GetString());
        Assert.DoesNotContain(ApiTools.SearchTool, names);
        Assert.DoesNotContain(ApiTools.GetTool, names);
    }
}

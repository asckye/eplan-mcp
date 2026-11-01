using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
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
        builder.Services.AddSingleton(new ActionCatalogReader(() => (null, null)));
        using IHost host = builder.Build();
        CancellationToken token = TestContext.Current.CancellationToken;
        await host.StartAsync(token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: token);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            Assert.Equal(ReviewedToolContract.For(actionRun: false, apiTry: false, actionTry: false), tools.Select(t => t.Name).Order(StringComparer.Ordinal));
            foreach (string name in new[] { ApiTools.SearchTool, ApiTools.GetTool, ActionCatalogTools.SearchTool, ActionCatalogTools.GetTool })
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
            var actions = ApiToolTests.Data(await client.CallToolAsync(ActionCatalogTools.SearchTool,
                new Dictionary<string, object?> { ["source"] = "official", ["tier"] = "A", ["text"] = "backup" }, cancellationToken: token));
            Assert.Equal("MASTERDATA", Assert.Single(actions.GetProperty("items").EnumerateArray()).GetProperty("type").GetString());
            var action = ApiToolTests.Data(await client.CallToolAsync(ActionCatalogTools.GetTool,
                new Dictionary<string, object?> { ["name"] = "backup" }, cancellationToken: token));
            Assert.Equal(2, action.GetProperty("total").GetInt32());
            Assert.NotEmpty(action.GetProperty("gaps").EnumerateArray());
            ApiToolTests.AssertError(await client.CallToolAsync(ActionCatalogTools.SearchTool,
                new Dictionary<string, object?> { ["misspelled"] = true }, cancellationToken: token));
            ApiToolTests.AssertError(await client.CallToolAsync(ActionCatalogTools.GetTool,
                new Dictionary<string, object?> { ["name"] = "missing" }, cancellationToken: token), ErrorReason.ResourceNotFound);
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
        Assert.DoesNotContain(options.ToolCollection!, t => t.ProtocolTool.Name is ActionCatalogTools.SearchTool or ActionCatalogTools.GetTool);
        var names = options.ToolCollection!.Single(t => t.ProtocolTool.Name == UsageTools.UsageTool).ProtocolTool.InputSchema
            .GetProperty("properties").GetProperty("tool").GetProperty("enum").EnumerateArray().Select(n => n.GetString());
        Assert.DoesNotContain(ApiTools.SearchTool, names);
        Assert.DoesNotContain(ApiTools.GetTool, names);
        Assert.DoesNotContain(ActionCatalogTools.SearchTool, names);
        Assert.DoesNotContain(ActionCatalogTools.GetTool, names);
    }

    [Fact]
    public async Task TrialValidationIncludesTemplatesBeforeSdkBindingAndBridgeDispatch()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var bridge = new FakeBridgeClient();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(HostConfig.Defaults(new(@"D:\test-data")) with { ApiTry = ActionRunSwitch.On, ActionTry = ActionRunSwitch.On });
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(bridge);
        builder.Services.AddSingleton(new ActionCatalogReader(() => (null, null)));
        using IHost host = builder.Build();
        CancellationToken token = TestContext.Current.CancellationToken;
        await host.StartAsync(token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: token);
            foreach (bool missing in new[] { false, true })
            {
                var api = new Dictionary<string, object?> { ["group"] = "O:Eplan.EplApi.DataModel.Page.GetWriteProtectionFlagSet", ["parameters"] = new { target = "bad", nKind = "wrong" } };
                var action = new Dictionary<string, object?> { ["action"] = "XEsGetPropertyAction", ["parameters"] = missing ? new[] { "bad" } : (object)new { Unknown = 1 } };
                if (!missing)
                { api["project"] = "Scratch"; action["project"] = "Scratch"; }
                foreach ((string tool, Dictionary<string, object?> args) in new[] { (ApiTryTools.Tool, api), (ActionTryTools.Tool, action) })
                {
                    CallToolResult result = await client.CallToolAsync(tool, args, cancellationToken: token);
                    Assert.True(result.IsError);
                    using JsonDocument document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
                    Assert.Equal(ErrorReason.InvalidArgument, document.RootElement.GetProperty("error").GetProperty("reason").GetString());
                    Assert.Equal(tool, document.RootElement.GetProperty("try_template").GetProperty("tool").GetString());
                }
            }
            Assert.Empty(bridge.Calls);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
}

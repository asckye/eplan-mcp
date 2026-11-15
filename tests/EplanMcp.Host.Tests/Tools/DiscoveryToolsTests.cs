using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>ADR-0057: the discovery profile and its dispatchers, on the tools as <c>serve</c> registers them; no EPLAN.</summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class DiscoveryToolsTests
{
    [Fact]
    public async Task Discovery_OffersFourTools_AndFullOffersNoneOfItsOwn()
    {
        await using ServiceProvider discovery = Provider(ToolProfile.Discovery);
        await using ServiceProvider full = Provider(ToolProfile.Full);

        Assert.Equal([DiscoveryTools.ReadTool, DiscoveryTools.SearchTool, UsageTools.UsageTool, DiscoveryTools.WriteTool], Names(discovery));
        Assert.DoesNotContain(Names(full), name => name is DiscoveryTools.SearchTool or DiscoveryTools.ReadTool or DiscoveryTools.WriteTool);
        Assert.Contains(PageTools.ListTool, Names(full));
    }

    [Fact]
    public async Task Search_FindsTheToolAndNamesTheDispatcherFromItsOwnAnnotations()
    {
        await using ServiceProvider provider = Provider(ToolProfile.Discovery);
        var registry = provider.GetRequiredService<ToolRegistry>();
        _ = Options(provider);

        JsonElement found = WriteToolHarness.Structured(DiscoveryTools.Search(registry, "page create"));

        JsonElement first = found.GetProperty("tools")[0];
        Assert.Equal((PageWriteTools.CreateTool, "write", DiscoveryTools.WriteTool),
            (first.GetProperty("name").GetString(), first.GetProperty("safety").GetString(), first.GetProperty("dispatcher").GetString()));
        Assert.DoesNotContain(found.GetProperty("tools").EnumerateArray(), t => ToolProfiles.Discovery.Contains(t.GetProperty("name").GetString()!));
        Assert.Equal(0, WriteToolHarness.Structured(DiscoveryTools.Search(registry, "zzqqxx")).GetProperty("total").GetInt32());
        Assert.True(DiscoveryTools.Search(registry, "", 0).IsError);
    }

    [Fact]
    public async Task Read_ReturnsExactlyWhatADirectCallReturns()
    {
        await using ServiceProvider provider = Provider(ToolProfile.Discovery);
        string arguments = """{"type": "Eplan.EplApi.DataModel.Page", "limit": 2}""";

        CallToolResult direct = await CallAsync(provider, ApiTools.SearchTool, arguments);
        CallToolResult dispatched = await CallAsync(provider, DiscoveryTools.ReadTool,
            Dispatch(ApiTools.SearchTool, arguments));

        Assert.NotEqual(true, direct.IsError);
        Assert.Equal(JsonSerializer.Serialize(direct), JsonSerializer.Serialize(dispatched));
    }

    [Fact]
    public async Task EachDispatcher_RefusesTheOthersTools_AndUnknownOnes()
    {
        await using ServiceProvider provider = Provider(ToolProfile.Discovery);

        string read = Violations(await CallAsync(provider, DiscoveryTools.ReadTool, $$"""{"tool": "{{PageWriteTools.CreateTool}}"}"""));
        string write = Violations(await CallAsync(provider, DiscoveryTools.WriteTool, $$"""{"tool": "{{PageTools.ListTool}}"}"""));
        string unknown = Violations(await CallAsync(provider, DiscoveryTools.ReadTool, """{"tool": "eplan_nothing"}"""));
        string self = Violations(await CallAsync(provider, DiscoveryTools.WriteTool, $$"""{"tool": "{{DiscoveryTools.WriteTool}}"}"""));

        Assert.Contains("call eplan_tool_write", read, StringComparison.Ordinal);
        Assert.Contains("call eplan_tool_read", write, StringComparison.Ordinal);
        Assert.Contains("find one with eplan_tool_search", unknown, StringComparison.Ordinal);
        Assert.Contains("find one with eplan_tool_search", self, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArgumentErrors_AreTheSameAsForADirectCall()
    {
        await using ServiceProvider provider = Provider(ToolProfile.Discovery);

        string direct = Violations(await CallAsync(provider, ApiTools.SearchTool, """{"limt": 2}"""));
        string dispatched = Violations(await CallAsync(provider, DiscoveryTools.ReadTool,
            Dispatch(ApiTools.SearchTool, """{"limt": 2}""")));
        string notAnObject = Violations(await CallAsync(provider, DiscoveryTools.ReadTool,
            Dispatch(ApiTools.SearchTool, "[1]")));

        Assert.Equal(direct, dispatched);
        Assert.Contains("'arguments' must be an object", notAnObject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Usage_InDiscovery_ExplainsEveryToolTheDispatchersReach()
    {
        await using ServiceProvider provider = Provider(ToolProfile.Discovery);
        McpServerOptions options = Options(provider);
        var registry = provider.GetRequiredService<ToolRegistry>();

        Assert.Contains(PageWriteTools.CreateTool, registry.Explained);
        ToolResultAssert.Succeeded(UsageTools.ToolUsage(Microsoft.Extensions.Options.Options.Create(options), PageWriteTools.CreateTool, registry: registry));
        JsonElement schema = options.ToolCollection!.Single(t => t.ProtocolTool.Name == DiscoveryTools.WriteTool).ProtocolTool.InputSchema;
        string[] reachable = [.. schema.GetProperty("properties").GetProperty("tool").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!)];
        Assert.Contains(PageWriteTools.CreateTool, reachable);
        Assert.DoesNotContain(PageTools.ListTool, reachable);
    }

    private static ServiceProvider Provider(ToolProfile profile)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()));
        services.AddSingleton(new ToolProfileChoice(profile));
        services.AddEplanMcp();
        return services.BuildServiceProvider();
    }

    private static McpServerOptions Options(ServiceProvider provider) => provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

    private static string[] Names(ServiceProvider provider) =>
        [.. Options(provider).ToolCollection!.Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal)];

    /// <summary>A tools/call as the server runs it: the argument filter, then the tool, with a real request context.</summary>
    private static async Task<CallToolResult> CallAsync(ServiceProvider provider, string name, string arguments)
    {
        McpServerOptions options = Options(provider);
        McpServerTool tool = provider.GetRequiredService<ToolRegistry>().All.Single(t => t.ProtocolTool.Name == name);
#pragma warning disable CA2000 // McpServer owns and disposes this in-memory transport.
        await using McpServer server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), options, serviceProvider: provider);
#pragma warning restore CA2000
        var context = new RequestContext<CallToolRequestParams>(server, new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" },
            new CallToolRequestParams { Name = name, Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)! })
        {
            MatchedPrimitive = tool,
            Services = provider,
        };
        return await UnknownArgumentFilter.Apply((c, t) => tool.InvokeAsync(c, t))(context, TestContext.Current.CancellationToken);
    }

    private static string Dispatch(string tool, string arguments) => "{\"tool\": \"" + tool + "\", \"arguments\": " + arguments + "}";

    private static string Violations(CallToolResult result)
    {
        JsonElement error = WriteToolHarness.Error(result);
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        return error.GetProperty("metadata").GetProperty("violations").GetString()!;
    }
}

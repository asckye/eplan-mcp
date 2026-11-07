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

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class MacroInsertRawInputTests
{
    [Theory]
    [InlineData("page")]
    [InlineData("macro")]
    [InlineData("part")]
    [InlineData("part_variant")]
    [InlineData("representation")]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("variant")]
    [InlineData("numbering")]
    [InlineData("macro_box")]
    [InlineData("values")]
    public async Task RegisteredTool_RejectsExplicitNullTopLevelItemFields(string field)
    {
        JsonElement error = await InvokeFilter($$"""
            {"project":"Line1","{{field}}":null,"items":[{"page":"P","macro":"M.ema","x":0,"y":10}]}
            """);

        Assert.StartsWith("/" + field + ":", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisteredTool_ReportsEveryMissingAndUnknownItemFieldWithJsonPointers()
    {
        JsonElement error = await InvokeFilter("""
            {
              "project":"Line1",
              "items":[
                {"macro":"M.ema","x":0,"y":10,"unexpected/~":true},
                {"page":"P","macro":"M.ema","y":10},
                {"page":"P","macro":"M.ema","x":0}
              ]
            }
            """);

        string violations = error.GetProperty("metadata").GetProperty("violations").GetString()!;
        Assert.Contains("/items/0/page:", violations, StringComparison.Ordinal);
        Assert.Contains("/items/0/unexpected~1~0:", violations, StringComparison.Ordinal);
        Assert.Contains("/items/1/x:", violations, StringComparison.Ordinal);
        Assert.Contains("/items/2/y:", violations, StringComparison.Ordinal);
        Assert.DoesNotContain("items[", violations, StringComparison.Ordinal);
        Assert.Equal(4, violations.Split('\n').Length);
    }

    private static async Task<JsonElement> InvokeFilter(string json)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()));
        services.AddEplanMcp();
        await using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        McpServerTool tool = options.ToolCollection!.Single(tool => tool.ProtocolTool.Name == MacroWriteTools.InsertTool);
#pragma warning disable CA2000 // McpServer owns and disposes this in-memory transport.
        await using McpServer server = McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), options, serviceProvider: provider);
#pragma warning restore CA2000
        var parameters = new CallToolRequestParams
        {
            Name = MacroWriteTools.InsertTool,
            Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!,
        };
        var context = new RequestContext<CallToolRequestParams>(server,
            new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" }, parameters)
        {
            MatchedPrimitive = tool,
        };
        bool invoked = false;
        McpRequestHandler<CallToolRequestParams, CallToolResult> handler = UnknownArgumentFilter.Apply((_, _) =>
        {
            invoked = true;
            return ValueTask.FromResult(new CallToolResult());
        });

        CallToolResult result = await handler(context, TestContext.Current.CancellationToken);

        Assert.False(invoked);
        JsonElement error = WriteToolHarness.Error(result);
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        return error;
    }
}

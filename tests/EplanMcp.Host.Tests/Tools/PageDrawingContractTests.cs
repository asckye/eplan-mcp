using System.Text.Json;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PageDrawingContractTests
{
    [Fact]
    public void RegisteredTool_ExposesOptionalDiscoveryWithUnchangedReadAnnotations()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        Tool tool = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Single(t => t.ProtocolTool.Name == PageTools.GetTool).ProtocolTool;
        JsonElement input = tool.InputSchema;
        Assert.Equal(["page"], input.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        JsonElement fields = input.GetProperty("properties");
        Assert.False(fields.GetProperty("include_drawings").GetProperty("default").GetBoolean());
        JsonElement limit = fields.GetProperty("drawing_limit");
        Assert.Equal((50, 1, 200), (limit.GetProperty("default").GetInt32(), limit.GetProperty("minimum").GetInt32(), limit.GetProperty("maximum").GetInt32()));
        Assert.Equal((true, false, true, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        JsonElement output = tool.OutputSchema!.Value;
        Assert.False(output.TryGetProperty("required", out JsonElement required) && required.EnumerateArray().Any(v => v.GetString() == "drawings"));
        JsonElement drawings = output.GetProperty("properties").GetProperty("drawings");
        Assert.Equal(200, drawings.GetProperty("properties").GetProperty("items").GetProperty("maxItems").GetInt32());
        Assert.DoesNotContain("execution", output.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }
}

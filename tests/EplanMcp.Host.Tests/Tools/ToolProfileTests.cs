using System.Text.Json;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>The tool profiles of mcp-tool-design 10, on the tools exactly as <c>serve</c> registers them.</summary>
[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ToolProfileTests
{
    [Fact]
    public void Core_HoldsAtMost25ToolsThatAllExist()
    {
        string[] all = [.. Tools(ToolProfile.Full).Select(t => t.Name)];

        Assert.InRange(ToolProfiles.Core.Count, 1, ToolProfiles.CoreLimit);
        Assert.All(ToolProfiles.Core, name => Assert.Contains(name, all));
    }

    [Fact]
    public void Core_HasNoDestructiveTool()
    {
        Assert.All(Tools(ToolProfile.Core), t => Assert.False(t.Annotations?.DestructiveHint, t.Name));
    }

    [Fact]
    public void Full_ListsEveryTool_AndCoreOnlyItsOwn()
    {
        Assert.Equal(31, Tools(ToolProfile.Full).Length);
        Assert.Equal(ToolProfiles.Core.Order(StringComparer.Ordinal), Tools(ToolProfile.Core).Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Core_OffersUsageOnlyForItsOwnTools()
    {
        JsonElement names = Tools(ToolProfile.Core).Single(t => t.Name == UsageTools.UsageTool).InputSchema.GetProperty("properties").GetProperty("tool").GetProperty("enum");

        Assert.Equal(ToolProfiles.Core.Order(StringComparer.Ordinal), names.EnumerateArray().Select(n => n.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.False, Tools(ToolProfile.Core).Single(t => t.Name == UsageTools.UsageTool).InputSchema.GetProperty("additionalProperties").ValueKind);
    }

    private static Tool[] Tools(ToolProfile profile)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(new ToolProfileChoice(profile));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Select(t => t.ProtocolTool)];
    }
}

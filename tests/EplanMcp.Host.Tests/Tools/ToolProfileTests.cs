using System.Text.Json;
using EplanMcp.Host.Safety;
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
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == ListImportTools.ImportTool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == ListImportTools.ImportTool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == CableGenerateTools.GenerateTool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == CableGenerateTools.GenerateTool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == PlcImportTools.Tool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == PlcImportTools.Tool);
    }

    [Fact]
    public void Full_ListsEveryTool_AndCoreOnlyItsOwn()
    {
        Assert.Equal(75, Tools(ToolProfile.Full).Length);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == PropertySetTools.Tool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == PropertySetTools.Tool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == TranslationImportTools.Tool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == TranslationImportTools.Tool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == PageTextReplaceTools.Tool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == PageTextReplaceTools.Tool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == PropertyGetTools.Tool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == PropertyGetTools.Tool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == PropertySchemaTools.Tool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == PropertySchemaTools.Tool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == PlanTools.GetTool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == PlanTools.GetTool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == ProjectCreateTools.CreateTool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == ContextTools.GetTool);
        Assert.Contains(Tools(ToolProfile.Full), t => t.Name == WorkflowTools.PlanTool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == WorkflowTools.PlanTool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == ContextTools.GetTool);
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == ProjectCreateTools.CreateTool);
        Assert.Equal(ToolProfiles.Core.Order(StringComparer.Ordinal), Tools(ToolProfile.Core).Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Core_OffersUsageOnlyForItsOwnTools()
    {
        JsonElement names = Tools(ToolProfile.Core).Single(t => t.Name == UsageTools.UsageTool).InputSchema.GetProperty("properties").GetProperty("tool").GetProperty("enum");

        Assert.Equal(ToolProfiles.Core.Order(StringComparer.Ordinal), names.EnumerateArray().Select(n => n.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.False, Tools(ToolProfile.Core).Single(t => t.Name == UsageTools.UsageTool).InputSchema.GetProperty("additionalProperties").ValueKind);
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 0)]
    public void PackResourceTemplates_AreInTheFullProfileOnly(bool full, int count)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(new ToolProfileChoice(full ? ToolProfile.Full : ToolProfile.Core));
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()) with { ActionRun = ActionRunSwitch.On });
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(count, provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ResourceCollection?.Count ?? 0);
    }

    private static Tool[] Tools(ToolProfile profile)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(new ToolProfileChoice(profile));
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()) with { ActionRun = ActionRunSwitch.On });
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Select(t => t.ProtocolTool)];
    }
}

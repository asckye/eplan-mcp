using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Trait("Tool", PlanApplyTools.ApplyTool)]
[Trait("Tool", PlanApplyTools.TaskTool)]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlanApplyToolTests
{
    private static readonly string[] _pages = ["+A/1"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_StaticDestructiveAnnotation_DoesNotOverrideServerCatalogTier(bool destructive)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Tool apply = options.ToolCollection!.Single(t => t.ProtocolTool.Name == PlanApplyTools.ApplyTool).ProtocolTool;
        Assert.Equal((false, true, false, false), (apply.Annotations!.ReadOnlyHint, apply.Annotations.DestructiveHint, apply.Annotations.IdempotentHint, apply.Annotations.OpenWorldHint));
        Assert.Equal("Apply plan", apply.Title);
        PlanArgumentCheck input = PlanArguments.Check(JsonSerializer.SerializeToElement(new
        {
            project = "Line1",
            steps = new[] { new { op = destructive ? "page.delete" : "page.create", args = destructive ? (object)new { pages = _pages } : new { pages = new[] { new { name = "+A/1" } } } } },
        }), null, "en_US");
        Assert.Empty(input.Violations);
        ToolTier tier = input.Steps.Max(s => s.Tier);
        Assert.Equal(destructive ? ToolTier.Destructive : ToolTier.Write, tier);
        Assert.Equal(destructive ? PolicyVerdict.NeedsApproval : PolicyVerdict.Allow, PolicyEngine.Decide(tier, true, HostConfig.Defaults(new("unused"))).Verdict);
        foreach (string tool in new[] { PlanApplyTools.ApplyTool, PlanApplyTools.TaskTool })
        {
            Tool definition = options.ToolCollection!.Single(t => t.ProtocolTool.Name == tool).ProtocolTool;
            JsonElement wait = definition.InputSchema.GetProperty("properties").GetProperty("wait_seconds");
            Assert.Equal(90, wait.GetProperty("default").GetInt32());
            Assert.Equal(0, wait.GetProperty("minimum").GetInt32());
            Assert.Equal(110, wait.GetProperty("maximum").GetInt32());
        }
        Tool query = options.ToolCollection!.Single(t => t.ProtocolTool.Name == PlanApplyTools.TaskTool).ProtocolTool;
        Assert.Equal((true, false, true, false), (query.Annotations!.ReadOnlyHint, query.Annotations.DestructiveHint, query.Annotations.IdempotentHint, query.Annotations.OpenWorldHint));
        Assert.Equal(8, query.OutputSchema!.Value.GetProperty("anyOf")[0].GetProperty("properties").GetProperty("status").GetProperty("enum").GetArrayLength());
    }
}

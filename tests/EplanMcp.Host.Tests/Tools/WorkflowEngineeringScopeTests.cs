using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class WorkflowEngineeringScopeTests
{
    [Theory]
    [InlineData("function")]
    [InlineData("connection")]
    [InlineData("new_name")]
    public void CabinetScope_WholePageContainedButObjectOrNewNameOutside_Rejects(string kind)
    {
        (WorkflowEngineering context, ProjectBinding binding) = Context();
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        SnapshotPage page = baseline.Snapshot.Pages.Single(p => p.Name == "+A1/1");
        PlanStep step = PlanTestData.Definition().Steps[0] with { Op = "page.update", ReadScope = PlanScopeRules.Pages([page.Name]), ModifyScope = PlanScopeRules.Pages([page.Name]), Preconditions = [], PlannedChanges = [] };
        if (kind == "function")
        { page.Objects.Single(o => o.Kind == "function").Name = "+A10-K1"; }
        if (kind == "connection")
        { page.Objects.Add(new() { Kind = "connection", Page = page.Name, Start = new() { Function = "+A1-K1" }, End = new() { Function = "+A10-K1" } }); }
        if (kind == "new_name")
        { step = step with { PlannedChanges = [new("update", "device", "+A1-K1", "tag", "+A1-K1", "+A10-K1")] }; }

        BridgeCallException error = Assert.Throws<BridgeCallException>(() => context.Guard(binding, baseline, [step]));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal("/cabinet", error.Error.Metadata!["pointer"]);
    }

    [Fact]
    public void CabinetScope_ContainedPageAndObjects_IsAcceptedWithoutTrimming()
    {
        (WorkflowEngineering context, ProjectBinding binding) = Context();
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanStep step = PlanTestData.Definition().Steps[0] with { Op = "page.update", ReadScope = PlanScopeRules.Pages(["+A1/1"]), ModifyScope = PlanScopeRules.Pages(["+A1/1"]), Preconditions = [] };
        context.Guard(binding, baseline, [step]);
        Assert.Equal(["+A1/1"], step.ModifyScope.Pages);
    }

    [Fact]
    public void WorkflowContract_HasExclusiveInputsOptionalEngineeringAndCompactDescriptions()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        Tool tool = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Single(t => t.ProtocolTool.Name == WorkflowTools.PlanTool).ProtocolTool;
        Assert.Equal(2, tool.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(["project"], tool.InputSchema.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.True(tool.InputSchema.GetProperty("properties").TryGetProperty("cabinet", out _));
        JsonElement output = tool.OutputSchema!.Value.GetProperty("anyOf")[0];
        Assert.True(output.GetProperty("properties").TryGetProperty("engineering", out _));
        Assert.DoesNotContain("engineering", output.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.InRange((tool.Description?.Length ?? 0) + DescriptionLength(tool.InputSchema), 0, 1000);
    }

    private static int DescriptionLength(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Sum(p => p.Name == "description" && p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()!.Length : DescriptionLength(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Sum(DescriptionLength),
        _ => 0,
    };

    [Fact]
    public void LargeEngineeringEvidence_ResultKeepsReferenceAndStoredDefinitionWithinBudget()
    {
        PlanDefinition definition = PlanEngineeringTests.Definition();
        definition = definition with { Engineering = definition.Engineering! with { Acceptance = JsonSerializer.SerializeToElement(new { evidence = Enumerable.Range(0, 100).Select(i => new { id = i, text = new string('a', 500) }) }) } };
        string before = PlanCanonical.Serialize(definition);
        var result = WorkflowResults.Success(new(2, PlanCanonical.Hash(definition), definition, new()));
        Assert.True(result.StructuredContent!.Value.GetProperty("truncated").GetBoolean());
        Assert.False(result.StructuredContent.Value.TryGetProperty("engineering", out _));
        Assert.Contains(definition.PlanId, result.StructuredContent.Value.GetProperty("resource").GetString()!, StringComparison.Ordinal);
        Assert.InRange(ResultShaper.ResultTokens(result.StructuredContent.Value.GetRawText()), 1, 5000);
        Assert.Equal(before, PlanCanonical.Serialize(definition));
    }
    private static (WorkflowEngineering, ProjectBinding) Context()
    {
        var cabinet = new CabinetBinding { Id = "a1", Layers = [], Values = new(), Structure = JsonNode.Parse("""{"prefixes":[{"location":"+A1"}]}""")!.AsObject() };
        return (new(cabinet, [], [], [], JsonSerializer.SerializeToElement(new { items = new { } }), null, JsonSerializer.SerializeToElement(new { })),
            new() { BindingVersion = 2, Project = ContextTestSnapshot.ProjectPath, Revision = 1, Cabinets = [cabinet], PackRoots = [], ProjectValues = new() });
    }
}

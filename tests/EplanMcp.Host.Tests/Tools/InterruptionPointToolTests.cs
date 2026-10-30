using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class InterruptionPointToolTests
{
    private const string Evidence = "2 undo steps; LINK; counterpart_pages=[peer]; native peer oid=90";
    private static JsonElement Item => JsonSerializer.SerializeToElement(new { kind = "interruption_point", name = "LINK", direction = "Up", position = (int[])[60, 60], counterpart_pages = (string[])["peer"] });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitScopeAndNativeReferences_RetainedByWritePipeline(bool dry)
    {
        using var harness = Setup();
        JsonElement result = WriteToolHarness.Structured(await Call(harness, dry));
        Assert.Equal(dry, result.GetProperty("dryRun").GetBoolean());
        if (dry)
        { Assert.Empty(harness.Bridge.Writes); }
        else
        {
            Assert.Single(harness.Bridge.Writes);
            Assert.Equal("native:/2.4", result.GetProperty("interruptionPoints")[0].GetProperty("crossReference").GetString());
            Assert.Equal("peer", result.GetProperty("interruptionPoints")[0].GetProperty("counterparts")[0].GetProperty("page").GetString());
            JsonElement sent = JsonSerializer.SerializeToElement(harness.Bridge.Calls.Last().Args);
            Assert.Equal(Item.GetRawText(), sent.GetProperty("request").GetProperty("items")[0].GetRawText());
        }
    }

    [Fact]
    public async Task OutsideScratch_ApprovalIncludesExactScopeAndPairEvidence_DenialNeverWrites()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Denied);
        using var harness = Setup(channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "outside", "Line1");
        JsonElement error = WriteToolHarness.Error(await Call(harness));
        Assert.Equal(ErrorReason.ApprovalDenied, error.GetProperty("reason").GetString());
        Assert.Contains(Evidence, Assert.Single(WritePipeline.ChangeLines(Assert.Single(channel.Questions).Plan)), StringComparison.Ordinal);
        Assert.Empty(harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)]
    public async Task CounterpartChangedOrReadbackFailure_NoPartialSuccessOrReplay(string reason, string execution)
    {
        using var harness = Setup();
        harness.Bridge.WriteFailure = new() { Reason = reason, Execution = execution, Message = "Native counterpart changed." };
        var result = await Call(harness);
        JsonElement error = WriteToolHarness.Error(result);
        Assert.Equal(execution, error.GetProperty("execution").GetString());
        Assert.Null(result.StructuredContent);
        Assert.Single(harness.Bridge.Writes);
    }

    private static WriteToolHarness Setup(params IApprovalChannel[] channels)
    {
        var harness = new WriteToolHarness(channels);
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "connections", Name = "page", NewValue = Evidence }]);
            plan.Details = new() { ["preview"] = "{\"affectedPages\":[\"page\",\"peer\"],\"undoSteps\":2}" };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => JsonNode.Parse("{\"affectedPages\":[\"page\",\"peer\"],\"undoSteps\":2,\"interruptionPoints\":[{\"name\":\"LINK\",\"crossReference\":\"native:/2.4\",\"counterparts\":[{\"page\":\"peer\",\"oid\":90}]}]}")!;
        return harness;
    }
    private static Task<ModelContextProtocol.Protocol.CallToolResult> Call(WriteToolHarness harness, bool dry = false) => ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge,
        "Line1", "page", [Item], dry_run: dry, cancellationToken: TestContext.Current.CancellationToken);
}

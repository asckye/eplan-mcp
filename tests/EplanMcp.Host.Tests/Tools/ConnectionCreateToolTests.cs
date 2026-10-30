using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ConnectionCreateToolTests
{
    [Fact]
    public async Task FakeBridge_Success_UsesOneWriteRequestWithFingerprintAndTwoUndoSteps()
    {
        using var harness = Setup();
        JsonElement result = WriteToolHarness.Structured(await Call(harness));
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(2, result.GetProperty("undoSteps").GetInt32());
        Assert.Single(harness.Bridge.Writes);
        JsonElement sent = JsonSerializer.SerializeToElement(harness.Bridge.Calls.Last().Args);
        Assert.Equal("f1", sent.GetProperty("fingerprint").GetString());
        Assert.True(sent.GetProperty("request").TryGetProperty("items", out _));
    }

    [Theory]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    public async Task FakeBridge_GenerationFailureOrStale_ReportsExecutionWithoutPartialData(string reason, string execution)
    {
        using var harness = Setup();
        harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Execution = execution, Message = "Creation failed." };
        var result = await Call(harness);
        JsonElement error = WriteToolHarness.Error(result);
        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Null(result.StructuredContent);
        Assert.Single(harness.Bridge.Writes);
    }

    [Fact]
    public async Task DryRun_ReadsPlanAndNeverWrites()
    {
        using var harness = Setup();
        JsonElement result = WriteToolHarness.Structured(await Call(harness, dry: true));
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.Empty(harness.Bridge.Writes);
    }

    [Fact]
    public async Task CableDryRun_ReturnsCompleteCoreImpactWithNoApprovalOrWrites()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Denied);
        using var harness = Setup(channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "Outside", "Line1");
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "connections", Name = "page", NewValue = "6 undo steps" }]);
            plan.Details = new() { ["preview"] = """{"project":"Line1","page":"page","execution":"not_started","undoSteps":6,"coreImpact":[{"connection":{"item_index":0},"designation":"-W3","label":"1","colour":"BK"}]}""" };
            return plan;
        };
        var result = WriteToolHarness.Structured(await ConnectionCreateTools.CreateAsync(harness.Pipeline, "Line1", "page",
            [JsonSerializer.SerializeToElement(new { kind = "cable_definition", points = (int[][])[[0, 16], [40, 16]], designation = "-W3", type_text = "Fictional cable", core_count = 1, cross_section_mm2 = 1.5, cores = new[] { new { connection = new { locator = new { project = "scratch-project", page = "page", type = 9, oid = 9, digest = new string('c', 64) } }, label = "1", colour = "BK" } } })],
            dry_run: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(6, result.GetProperty("undoSteps").GetInt32());
        Assert.Equal("-W3", Assert.Single(result.GetProperty("coreImpact").EnumerateArray()).GetProperty("designation").GetString());
        Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
        Assert.Empty(harness.Bridge.Writes);
        Assert.Empty(channel.Questions);
    }

    [Fact]
    public async Task CableExecute_ReportsActualCoresAndUndoCountThroughExistingPipeline()
    {
        using var harness = Setup();
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => JsonNode.Parse("""{"project":"Line1","page":"page","undoSteps":6,"actualCores":[{"designation":"+A1-W9","type":"Fictional cable","oid":9,"pins":["17/1/0","17/2/0"],"label":"1","colour":"BK"}]}""")!;
        var call = await ConnectionCreateTools.CreateAsync(harness.Pipeline, "Line1", "page",
            [JsonSerializer.SerializeToElement(new { kind = "cable_definition" })], cancellationToken: TestContext.Current.CancellationToken);
        JsonElement result = WriteToolHarness.Structured(call);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(6, result.GetProperty("undoSteps").GetInt32());
        Assert.Equal("BK", Assert.Single(result.GetProperty("actualCores").EnumerateArray()).GetProperty("colour").GetString());
        Assert.Single(harness.Bridge.Writes);
    }

    [Fact]
    public async Task CableExecute_UsesExistingPipeline_ApprovalListsSixUndoStepsAndDenialDoesNotWrite()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Denied);
        using var harness = Setup(channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "Outside", "Line1");
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = _ => harness.Plan([new() { Action = "create", Kind = "connections", Name = "page", NewValue = "6 undo steps; cable -W9 cores 1 BK, 2 BU, 3 GNYE" }]);
        JsonElement error = WriteToolHarness.Error(await ConnectionCreateTools.CreateAsync(harness.Pipeline, "Line1", "page",
            [JsonSerializer.SerializeToElement(new { kind = "cable_definition" })], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Contains("6 undo steps", Assert.Single(WritePipeline.ChangeLines(Assert.Single(channel.Questions).Plan)), StringComparison.Ordinal);
        Assert.Empty(harness.Bridge.Writes);
    }

    [Fact]
    public async Task OversizedEvidence_IsCleanedAndClipped_WithoutChangingExecutedArguments()
    {
        using var harness = Setup();
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => new JsonObject
        {
            ["project"] = "Line\u202e1",
            ["undoSteps"] = 2,
            ["symbols"] = new JsonArray([.. Enumerable.Range(0, 100).Select(_ => (JsonNode?)JsonValue.Create(new string('x', 500)))])
        };
        var result = WriteToolHarness.Structured(await Call(harness));
        Assert.Equal("Line1", result.GetProperty("project").GetString());
        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.InRange(ResultShaper.EstimateTokens(result.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
        Assert.Single(harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_ApprovalShowsTwoUndoSteps_AndDenialDoesNotWrite()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Denied);
        using var harness = Setup(channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "Outside", "Line1");
        JsonElement error = WriteToolHarness.Error(await Call(harness));
        Assert.Equal(ErrorReason.ApprovalDenied, error.GetProperty("reason").GetString());
        Assert.Contains("2 undo steps", Assert.Single(WritePipeline.ChangeLines(Assert.Single(channel.Questions).Plan)), StringComparison.Ordinal);
        Assert.Empty(harness.Bridge.Writes);
    }

    private static WriteToolHarness Setup(params IApprovalChannel[] channels)
    {
        var harness = new WriteToolHarness(channels);
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "connections", Name = "page", NewValue = "2 undo steps" }]);
            plan.Details = new() { ["preview"] = "{\"project\":\"Line1\",\"execution\":\"not_started\",\"undoSteps\":2}" };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => new JsonObject { ["project"] = "Line1", ["undoSteps"] = 2 };
        return harness;
    }
    private static Task<ModelContextProtocol.Protocol.CallToolResult> Call(WriteToolHarness harness, bool dry = false) => ConnectionCreateTools.CreateAsync(harness.Pipeline,
        "Line1", "page", [JsonSerializer.SerializeToElement(new { kind = "corner", position = (int[])[24, 16], directions = (string[])["Left", "Up"] })], dry_run: dry, cancellationToken: TestContext.Current.CancellationToken);
}

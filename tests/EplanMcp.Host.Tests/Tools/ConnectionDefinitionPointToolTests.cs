using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ConnectionDefinitionPointToolTests
{
    private static readonly int[] _position100 = [100, 100];
    private static JsonElement Item => JsonSerializer.SerializeToElement(new { kind = "definition_point", position = (int[])[40, 40], color = "RD", cross_section = "1.5" });
    private static JsonElement WireItem => JsonSerializer.SerializeToElement(new
    {
        kind = "wire",
        from = new { locator = new { oid = 1 }, pin_index = 0 },
        to = new { locator = new { oid = 2 }, pin_index = 0 },
        points = (int[][])[[20, 40], [60, 40]],
        definition_point = new { color = "RD", cross_section = "1.5" },
    });
    private const string Data = """{"project":"Line1","page":"page","undoSteps":1,"definitionPoints":[{"itemIndex":0,"type":"ConnectionDefinitionPoint","position":[40,40],"symbol":{"library":"SPECIAL","name":"CDP","variant":0},"properties":{"color":"RD","cross_section":"1.5"},"connectionValues":{"color":"RD","cross_section":"1.5"}}]}""";

    [Theory]
    [InlineData(null, false)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task PreviewAndExecute_RetainDefinitionPointsAndForwardExactRequest(bool? dry, bool existing)
    {
        using var harness = Setup();
        JsonElement item = existing ? Item : WireItem;
        CallToolResult call = dry is null
            ? await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [item], cancellationToken: TestContext.Current.CancellationToken)
            : await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [item], dry_run: dry.Value, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement result = WriteToolHarness.Structured(call);
        Assert.Equal(dry != false, result.GetProperty("dryRun").GetBoolean());
        Assert.Equal(dry == false ? ExecutionState.Committed : ExecutionState.NotStarted, result.GetProperty("execution").GetString());
        Assert.Equal(1, result.GetProperty("undoSteps").GetInt32());
        Assert.Equal("RD", result.GetProperty("definitionPoints")[0].GetProperty("properties").GetProperty("color").GetString());
        Assert.Equal("1.5", result.GetProperty("definitionPoints")[0].GetProperty("connectionValues").GetProperty("cross_section").GetString());
        Assert.Equal(dry == false ? 1 : 0, harness.Bridge.Writes.Count);
        if (dry == false)
        {
            JsonElement sent = JsonSerializer.SerializeToElement(harness.Bridge.Calls.Last().Args);
            Assert.Equal(item.GetRawText(), sent.GetProperty("request").GetProperty("items")[0].GetRawText());
        }
        else
        { Assert.Contains("dry_run: false", result.GetProperty("hint").GetString(), StringComparison.Ordinal); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PotentialDefinition_ForwardsNamesAndReportsWriteFailure(bool fail)
    {
        using var harness = Setup();
        if (fail) { harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.EplanOperationFailed, Execution = ExecutionState.RolledBack, Message = "potential readback" }; }
        JsonElement item = JsonSerializer.SerializeToElement(new { kind = "potential_definition", position = _position100, potential_name = "N", signal_name = "S" });
        CallToolResult result = await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [item], dry_run: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(harness.Bridge.Writes);
        if (fail) { Assert.Equal(ExecutionState.RolledBack, WriteToolHarness.Error(result).GetProperty("execution").GetString()); }
        else { Assert.Equal(ExecutionState.Committed, WriteToolHarness.Structured(result).GetProperty("execution").GetString()); }
        JsonElement sent = JsonSerializer.SerializeToElement(harness.Bridge.Calls.Last().Args);
        Assert.Equal(item.GetRawText(), sent.GetProperty("request").GetProperty("items")[0].GetRawText());
    }

    [Fact]
    public async Task ExistingConnection_ReadbackMismatchReportsRollbackWithoutRetry()
    {
        using var harness = Setup();
        harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = ErrorReason.EplanOperationFailed,
            Execution = ExecutionState.RolledBack,
            Message = "Definition point readback differs at definitionPoints/0/connectionValues/color.",
        };
        CallToolResult call = await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [Item], dry_run: false, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement error = WriteToolHarness.Error(call);
        Assert.Equal(ExecutionState.RolledBack, error.GetProperty("execution").GetString());
        Assert.Contains("connectionValues/color", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Null(call.StructuredContent);
        Assert.Single(harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_PreviewSkipsApprovalAndExecutionListsPointProperties()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Denied);
        using var harness = Setup(channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "outside", "Line1");
        await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [Item], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(channel.Questions);
        JsonElement error = WriteToolHarness.Error(await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [Item], dry_run: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.ApprovalDenied, error.GetProperty("reason").GetString());
        Assert.Contains("CDP RD 1.5", Assert.Single(WritePipeline.ChangeLines(Assert.Single(channel.Questions).Plan)), StringComparison.Ordinal);
        Assert.Empty(harness.Bridge.Writes);
    }

    [Fact]
    public async Task OversizedReadback_KeepsCompleteFirstPointAndReportsOriginalCount()
    {
        using var harness = Setup();
        JsonObject first = JsonNode.Parse(Data)!["definitionPoints"]![0]!.AsObject();
        first["properties"]!["designation"] = new string('d', 200);
        first["properties"]!["color"] = new string('R', 200);
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => new JsonObject
        {
            ["project"] = "Line1",
            ["page"] = "page",
            ["undoSteps"] = 1,
            ["symbols"] = new JsonArray([.. Enumerable.Range(0, 100).Select(_ => (JsonNode?)JsonValue.Create(new string('x', 500)))]),
            ["definitionPoints"] = new JsonArray([.. Enumerable.Range(0, 50).Select(_ => first.DeepClone())]),
        };
        JsonElement result = WriteToolHarness.Structured(await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [Item], dry_run: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.Equal(50, result.GetProperty("definitionPointCount").GetInt32());
        Assert.InRange(result.GetProperty("definitionPoints").GetArrayLength(), 1, 49);
        Assert.False(result.TryGetProperty("symbols", out _));
        Assert.Equal(first.ToJsonString(), JsonNode.Parse(result.GetProperty("definitionPoints")[0].GetRawText())!.ToJsonString());
        Assert.InRange(ResultShaper.EstimateTokens(result.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
    }

    private static WriteToolHarness Setup(params IApprovalChannel[] channels)
    {
        var harness = new WriteToolHarness(channels);
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "definition_point", Name = "CDP RD 1.5", NewValue = "[40,40]; 1 undo step" }]);
            plan.Details = new() { ["preview"] = Data };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => JsonNode.Parse(Data)!;
        return harness;
    }
}

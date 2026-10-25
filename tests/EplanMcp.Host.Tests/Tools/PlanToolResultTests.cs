using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlanToolResultTests
{
    [Theory]
    [InlineData(ErrorReason.PolicyDenied)]
    [InlineData(ErrorReason.BackupFailed)]
    [InlineData(ErrorReason.WritesSuspended)]
    [InlineData(ErrorReason.PlanStale)]
    public void PreAdmissionError_PreservesStandardFieldsAndExecution(string reason)
    {
        CallToolResult call = PlanToolResults.Failure(new BridgeCallException(new BridgeError { Reason = reason, Message = "Inspect the original plan." }), true);
        JsonElement data = call.StructuredContent!.Value;
        Assert.True(call.IsError);
        Assert.Equal("not_started", data.GetProperty("execution").GetString());
        Assert.Equal(reason, data.GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(7, data.GetProperty("error").EnumerateObject().Count());
        CallToolResult query = PlanToolResults.Failure(new TaskRegistryException(new(reason, "Inspect task.", false, null, [], "id")), false);
        Assert.False(query.StructuredContent!.Value.TryGetProperty("execution", out _));
    }

    [Theory]
    [InlineData(ExecutionState.Committed, "failed", null)]
    [InlineData(ExecutionState.Unknown, "skipped", ErrorReason.OutcomeUnknown)]
    [InlineData(ExecutionState.RolledBack, "skipped", ErrorReason.LockFailed)]
    public void TerminalFailure_KeepsEngineeringFactsAndStandardError(string execution, string verification, string? reason)
    {
        PlanDefinition definition = PlanTestData.Definition();
        PlanResult result = TaskTestData.Result(execution, verification, reason);
        var record = new PlanRecord(1, "hash", definition, new(Result: result));
        CallToolResult call = PlanToolResults.Plan(record, false);
        JsonElement data = call.StructuredContent!.Value;
        Assert.True(call.IsError);
        Assert.Equal(execution, data.GetProperty("execution").GetString());
        Assert.Equal(execution, data.GetProperty("steps")[0].GetProperty("execution").GetString());
        Assert.Equal(verification, data.GetProperty("verification").GetString());
        if (execution == ExecutionState.Unknown)
        {
            Assert.False(data.GetProperty("error").GetProperty("retryable").GetBoolean());
        }
        Assert.True(JsonElement.DeepEquals(data, JsonSerializer.Deserialize<JsonElement>(((TextContentBlock)call.Content[1]).Text)));
    }

    [Fact]
    public void LargeDifferences_AreBudgeted_WithCountsBackupAndResultReferenceRetained()
    {
        PlanDefinition definition = PlanTestData.Definition();
        PlanResult result = TaskTestData.Result() with
        {
            Backups = [new("backup.elk", 1, 0.1m, true)],
            Diff = JsonSerializer.SerializeToElement(Enumerable.Range(0, 200).Select(i => new { step = i, text = new string('x', 500) })),
        };
        CallToolResult call = PlanToolResults.Plan(new(1, "hash", definition, new(Result: result)), false);
        JsonElement data = call.StructuredContent!.Value;
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Equal("backup.elk", data.GetProperty("backup").GetString());
        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.NotEmpty(data.GetProperty("completedSteps").EnumerateArray());
        Assert.Contains("eplan_plan_get", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.True(ResultShaper.ResultTokens(data.GetRawText()) + ResultShaper.EstimateTokens(((TextContentBlock)call.Content[0]).Text) <= ResultShaper.DefaultTokenBudget);
    }
}

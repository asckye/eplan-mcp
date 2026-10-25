using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanSettlementFoldTests
{
    [Theory]
    [InlineData("committed", "committed", "Committed")]
    [InlineData("committed", null, "PartiallyCommitted")]
    [InlineData("committed", "rolled_back", "PartiallyCommitted")]
    [InlineData("rolled_back", null, "RolledBack")]
    [InlineData("unknown", null, "Unknown")]
    [InlineData("committed", "unknown", "Unknown")]
    [InlineData(null, null, "NotStarted")]
    public void SettledSteps_AggregateFiveValues_AndKeepUnsentSteps(string? first, string? second, string expected)
    {
        PlanRecord record = Record();
        var entries = new List<JsonObject>();
        foreach ((string? execution, int index) in new[] { (first, 1), (second, 2) })
        {
            if (execution is not null)
            {
                entries.Add(Entry("execution_started", index));
                JsonObject ended = Entry("execution_result", index);
                ended["execution"] = execution;
                ended["reason"] = execution == "rolled_back" ? ErrorReason.EplanOperationFailed : null;
                entries.Add(ended);
            }
        }

        PlanResult result = PlanSettlement.Fold(record, entries)!;

        Assert.Equal(expected, result.Execution.ToString());
        Assert.Equal(3, result.Steps.Length);
        Assert.Equal(ExecutionState.NotStarted, result.Steps[2].Execution);
        Assert.False(result.Steps[2].Effective);
        Assert.Equal("skipped", result.Verification);
        Assert.Contains("no after-evidence", result.Coverage!.Value.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Null(result.Validation);
        Assert.Null(result.Diff);
        if (expected == "Unknown")
        {
            Assert.Equal(ErrorReason.OutcomeUnknown, Assert.Single(result.Steps, s => s.Execution == ExecutionState.Unknown).Error!.Reason);
            Assert.False(result.Retryable);
        }
        if (first == ExecutionState.RolledBack)
        {
            Assert.Equal(ErrorReason.EplanOperationFailed, result.Steps[0].Error!.Reason);
        }
    }

    [Fact]
    public void StartedWithoutResult_EvenWithSuspension_RemainsPending()
    {
        Assert.Null(PlanSettlement.Fold(Record(), [Entry("execution_started", 1), Entry("writes_suspended", 1)]));
    }

    [Fact]
    public void ApprovedNeverStarted_AllStepsRemainNotStarted()
    {
        PlanRecord record = Record();
        record = record with { State = record.State.Approve(new(ApprovalDecision.Granted, "test", "binding")) };

        PlanResult result = PlanSettlement.Fold(record, [])!;

        Assert.Equal(PlanExecution.NotStarted, result.Execution);
        Assert.All(result.Steps, s => Assert.Equal(ExecutionState.NotStarted, s.Execution));
        Assert.False(result.Retryable);
    }

    [Theory]
    [InlineData("requestId")]
    [InlineData("step")]
    [InlineData("planId")]
    public void ResultMustMatchRequestPlanAndStep(string field)
    {
        JsonObject ended = Entry("execution_result", 1);
        ended["execution"] = ExecutionState.Committed;
        ended[field] = field == "step" ? JsonValue.Create(2) : JsonValue.Create("different");
        Assert.Null(PlanSettlement.Fold(Record(), [Entry("execution_started", 1), ended]));
    }

    [Theory]
    [InlineData("late")]
    [InlineData("recovered")]
    public void RecoveredCommit_KeepsRecoveryAndBackupFacts(string flag)
    {
        PlanRecord record = Record();
        JsonObject started = Entry("execution_started", 1);
        started["backup"] = "step-copy";
        JsonObject ended = Entry("execution_result", 1);
        ended["execution"] = ExecutionState.Committed;
        ended[flag] = true;
        JsonObject initial = Entry("plan_backup", 1);
        initial["backup"] = JsonSerializer.SerializeToNode(new PlanBackup("initial-copy", 2, 0.1m, true), PlanJson.Options);

        PlanResult result = PlanSettlement.Fold(record, [started, ended, initial])!;

        Assert.True(result.Steps[0].Recovered);
        Assert.Equal("AI: plan 01234567 1/3 page.update", result.Steps[0].UndoDescription);
        Assert.Equal("step-copy", result.Steps[0].Backup);
        Assert.Equal(new PlanBackup("initial-copy", 2, 0.1m, true), result.Backups[0]);
        Assert.Equal(new PlanBackup("step-copy", null, null, false), result.Backups[1]);
    }

    internal static PlanRecord Record()
    {
        PlanDefinition definition = PlanTestData.Definition();
        definition = definition with
        {
            Steps = [definition.Steps[0] with { Index = 1 }, definition.Steps[0] with { Index = 2 }, definition.Steps[0] with { Index = 3, Effective = false }],
        };
        return new(1, PlanCanonical.Hash(definition), definition, new PlanState().Admit("attempt", "lost-task", definition.Owner.RunId));
    }

    internal static JsonObject Entry(string kind, int index) => new()
    {
        ["kind"] = kind,
        ["planId"] = PlanTestData.Definition().PlanId,
        ["step"] = index,
        ["requestId"] = "request-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

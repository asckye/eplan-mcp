using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using TaskStatus = EplanMcp.Host.Plans.TaskStatus;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class TaskRecordTests
{
    [Theory]
    [InlineData("committed", "passed", null, false, false, "Succeeded")]
    [InlineData("committed", "failed", null, false, false, "Failed")]
    [InlineData("committed", "skipped", null, false, false, "Failed")]
    [InlineData("committed", null, null, false, false, "Failed")]
    [InlineData("committed", "passed", ErrorReason.Internal, false, false, "Failed")]
    [InlineData("rolled_back", "passed", null, false, false, "Failed")]
    [InlineData("not_started", "passed", null, false, false, "Failed")]
    [InlineData("committed", "passed", null, true, false, "Cancelled")]
    [InlineData("committed", "passed", null, false, true, "Interrupted")]
    [InlineData("unknown", "passed", ErrorReason.OutcomeUnknown, false, false, "Unknown")]
    [InlineData("unknown", "passed", ErrorReason.OutcomeUnknown, true, false, "Unknown")]
    [InlineData("unknown", "passed", ErrorReason.OutcomeUnknown, false, true, "Unknown")]
    [InlineData("unknown", "passed", ErrorReason.OutcomeUnknown, true, true, "Unknown")]
    public void StatusFor_ResultTable_SeparatesTaskStatusFromWriteFacts(
        string execution, string? verification, string? reason, bool cancelled, bool interrupted, string expected)
    {
        Assert.Equal(Enum.Parse<TaskStatus>(expected), TaskRecord.StatusFor(TaskTestData.Result(execution, verification, reason), cancelled, interrupted));
    }

    [Fact]
    public void StatusFor_NoChangesSucceeds_AndPartialCommitFailsEvenWithPassedVerification()
    {
        PlanResult noChanges = PlanResult.Aggregate([]) with { Verification = "passed" };
        Assert.Equal(TaskStatus.Succeeded, TaskRecord.StatusFor(noChanges));
        Assert.Equal(TaskStatus.Failed, TaskRecord.StatusFor(noChanges with { Verification = "skipped" }));
        PlanResult partial = PlanResult.Aggregate([new(0, "page.update", true, ExecutionState.Committed), new(1, "page.update", true, ExecutionState.NotStarted)])
            with
        { Verification = "passed" };
        Assert.Equal(PlanExecution.PartiallyCommitted, partial.Execution);
        Assert.Equal(TaskStatus.Failed, TaskRecord.StatusFor(partial));
    }

    [Theory]
    [InlineData("Queued", "queued")]
    [InlineData("WaitingApproval", "waiting_approval")]
    [InlineData("Running", "running")]
    [InlineData("Succeeded", "succeeded")]
    [InlineData("Failed", "failed")]
    [InlineData("Cancelled", "cancelled")]
    [InlineData("Interrupted", "interrupted")]
    [InlineData("Unknown", "unknown")]
    public void Status_UsesTheEightWireNames(string name, string wire)
    {
        TaskStatus value = Enum.Parse<TaskStatus>(name);
        Assert.Equal('"' + wire + '"', JsonSerializer.Serialize(value, HostJson.Options));
        Assert.Equal(value, JsonSerializer.Deserialize<TaskStatus>('"' + wire + '"', HostJson.Options));
    }
}

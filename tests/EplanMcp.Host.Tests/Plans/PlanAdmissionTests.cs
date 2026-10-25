using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanAdmissionTests
{
    [Fact]
    public void Admission_IsSingleUse_EvenWhileWaitingForApproval()
    {
        var compiled = new PlanState();
        PlanOrchestrator.RequireUnused(compiled);
        PlanState admitted = compiled.Admit("attempt", "task", "host");

        Assert.Equal(PlanStatus.Compiled, admitted.Status);
        Assert.Equal(1, admitted.Revision);
        BridgeCallException replay = Assert.Throws<BridgeCallException>(() => PlanOrchestrator.RequireUnused(admitted));
        Assert.Equal(ErrorReason.InvalidArgument, replay.Error.Reason);
        Assert.Contains("eplan_plan_get", replay.Error.Message, StringComparison.Ordinal);
        Assert.Null(compiled.AttemptId);
    }

    [Theory]
    [InlineData(ErrorReason.ApprovalDenied)]
    [InlineData(ErrorReason.PlanStale)]
    [InlineData(ErrorReason.BackupFailed)]
    [InlineData(ErrorReason.WritesSuspended)]
    public void AdmittedFailure_ConsumesPlan_WithNoStartedSteps(string reason)
    {
        PlanResult result = PlanResult.Aggregate([new(0, "page.create", true, ExecutionState.NotStarted)], new(reason, "Stopped", false));
        PlanState failed = new PlanState().Admit("attempt", "task", "host").Complete(result, PlanTestData.Created);

        Assert.Equal(PlanStatus.NotStarted, failed.Status);
        Assert.Equal("attempt", failed.AttemptId);
        Assert.Null(failed.StartedAt);
        Assert.Equal(PlanExecution.NotStarted, failed.Result!.Execution);
        Assert.Throws<BridgeCallException>(() => PlanOrchestrator.RequireUnused(failed));
    }
}

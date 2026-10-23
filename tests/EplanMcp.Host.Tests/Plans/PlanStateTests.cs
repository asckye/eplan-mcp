using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanStateTests
{
    [Fact]
    public void State_ApprovalAndAutomaticAllow_IncrementEachRevision()
    {
        var compiled = new PlanState();
        PlanState admitted = compiled.Admit("attempt", "task", "host");
        Assert.Equal(PlanStatus.Compiled, admitted.Status);
        Assert.Equal(1, admitted.Revision);
        Assert.Null(admitted.StartedAt);
        PlanState approved = admitted.Approve(new(ApprovalDecision.Granted, "workbench", "digest"));
        PlanState running = approved.Start(PlanTestData.Created, PolicyVerdict.NeedsApproval);
        Assert.Equal(2, approved.Revision);
        Assert.Equal(3, running.Revision);
        Assert.Equal(PlanStatus.Running, running.Status);
        PlanState automatic = admitted.Start(PlanTestData.Created, PolicyVerdict.Allow);
        Assert.Equal(2, automatic.Revision);
        Assert.Equal(PolicyVerdict.Allow, automatic.Policy);
        Assert.Null(automatic.Approval);
        Assert.Equal(0, compiled.Revision);
        Assert.Null(compiled.AttemptId);
    }

    [Theory]
    [InlineData("not_started", "NotStarted")]
    [InlineData("rolled_back", "RolledBack")]
    [InlineData("committed", "Committed")]
    [InlineData("unknown", "Unknown")]
    [InlineData("partial", "PartiallyCommitted")]
    public void Complete_AllFiveTerminalStates_RejectReplayAndFurtherTransitions(string execution, string expected)
    {
        PlanStepResult[] steps = execution == "partial"
            ? [new(0, "page.update", true, ExecutionState.Committed), new(1, "page.update", true, ExecutionState.NotStarted)]
            : [new(0, "page.update", true, execution, execution == ExecutionState.Unknown ? new(ErrorReason.OutcomeUnknown, "Unknown.", false) : null)];
        PlanResult result = PlanResult.Aggregate(steps);
        PlanState done = Admitted().Start(PlanTestData.Created, PolicyVerdict.Allow).Complete(result, PlanTestData.Created.AddSeconds(1));
        Assert.Equal(Enum.Parse<PlanStatus>(expected), done.Status);
        Assert.Equal(3, done.Revision);
        Assert.Equal(PlanTestData.Created.AddSeconds(1), done.CompletedAt);
        Assert.Same(result, done.Result);
        Assert.Throws<InvalidOperationException>(() => done.Admit("again", "task", "host"));
        Assert.Throws<InvalidOperationException>(() => done.Start(PlanTestData.Created, PolicyVerdict.Allow));
        Assert.Throws<InvalidOperationException>(() => done.Complete(result, PlanTestData.Created));
        Assert.Throws<InvalidOperationException>(() => done.Approve(new(ApprovalDecision.Granted, "workbench", "digest")));
    }

    [Fact]
    public void State_InvalidTransitionsAndPolicy_Reject()
    {
        var compiled = new PlanState();
        PlanState admitted = Admitted();
        var grant = new PlanApproval(ApprovalDecision.Granted, "workbench", "digest");
        PlanResult committed = PlanResult.Aggregate([new(0, "page.update", true, ExecutionState.Committed)]);
        Assert.Throws<InvalidOperationException>(() => compiled.Approve(grant));
        Assert.Throws<InvalidOperationException>(() => compiled.Start(PlanTestData.Created, PolicyVerdict.Allow));
        Assert.Throws<InvalidOperationException>(() => admitted.Admit("again", "task", "host"));
        Assert.Throws<InvalidOperationException>(() => admitted.Start(PlanTestData.Created, PolicyVerdict.NeedsApproval));
        Assert.Throws<InvalidOperationException>(() => admitted.Start(PlanTestData.Created, PolicyVerdict.Deny));
        Assert.Throws<InvalidOperationException>(() => admitted.Approve(grant with { Decision = ApprovalDecision.Denied }));
        Assert.Throws<InvalidOperationException>(() => admitted.Complete(committed, PlanTestData.Created));
        Assert.Throws<InvalidOperationException>(() => admitted.Approve(grant).Approve(grant));
        Assert.Throws<InvalidOperationException>(() => admitted.Approve(grant).Start(PlanTestData.Created, PolicyVerdict.Allow));
        Assert.Throws<InvalidOperationException>(() => admitted.Start(PlanTestData.Created, PolicyVerdict.Allow).Complete(committed, PlanTestData.Created.AddTicks(-1)));
    }

    [Fact]
    public void Complete_AdmissionOrApprovalFailure_IsConsumedNotStarted()
    {
        PlanResult refused = PlanResult.Aggregate([], new(ErrorReason.BackupFailed, "Backup failed.", false));
        PlanState state = Admitted().Complete(refused, PlanTestData.Created);
        Assert.Equal(PlanStatus.NotStarted, state.Status);
        Assert.Null(state.StartedAt);
        Assert.Equal(2, state.Revision);
        Assert.Throws<InvalidOperationException>(() => state.Admit("again", "task", "host"));
    }

    private static PlanState Admitted() => new PlanState().Admit("attempt", "task", "host");
}

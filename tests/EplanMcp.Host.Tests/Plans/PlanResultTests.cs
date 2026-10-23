using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanResultTests
{
    [Theory]
    [InlineData("not_started", "not_started", "NotStarted")]
    [InlineData("not_started", "rolled_back", "RolledBack")]
    [InlineData("not_started", "committed", "PartiallyCommitted")]
    [InlineData("not_started", "unknown", "Unknown")]
    [InlineData("rolled_back", "not_started", "RolledBack")]
    [InlineData("rolled_back", "rolled_back", "RolledBack")]
    [InlineData("rolled_back", "committed", "PartiallyCommitted")]
    [InlineData("rolled_back", "unknown", "Unknown")]
    [InlineData("committed", "not_started", "PartiallyCommitted")]
    [InlineData("committed", "rolled_back", "PartiallyCommitted")]
    [InlineData("committed", "committed", "Committed")]
    [InlineData("committed", "unknown", "Unknown")]
    [InlineData("unknown", "not_started", "Unknown")]
    [InlineData("unknown", "rolled_back", "Unknown")]
    [InlineData("unknown", "committed", "Unknown")]
    [InlineData("unknown", "unknown", "Unknown")]
    public void Aggregate_EveryPair_FollowsTheResultTable(string first, string second, string expected)
    {
        PlanResult result = PlanResult.Aggregate([Step(0, first), Step(1, second)]);
        Assert.Equal(Enum.Parse<PlanExecution>(expected), result.Execution);
        Assert.Equal(result.Execution == PlanExecution.Unknown, result.Error?.Reason == ErrorReason.OutcomeUnknown);
    }

    [Fact]
    public void Aggregate_NoEffectiveSteps_AndDryRun_AreNotStarted()
    {
        Assert.Equal(PlanExecution.NotStarted, PlanResult.Aggregate([]).Execution);
        PlanStepResult ignored = Step(0, ExecutionState.Unknown) with { Effective = false };
        Assert.Equal(PlanExecution.NotStarted, PlanResult.Aggregate([ignored]).Execution);
        Assert.Equal(PlanExecution.Committed, PlanResult.Aggregate([ignored, Step(1, ExecutionState.Committed)]).Execution);
        Assert.Equal(PlanExecution.NotStarted, PlanResult.Aggregate([Step(0, ExecutionState.NotStarted)]).Execution);
    }

    [Theory]
    [InlineData("not_started", true, true)]
    [InlineData("not_started", false, false)]
    [InlineData("rolled_back", true, true)]
    [InlineData("rolled_back", false, false)]
    [InlineData("committed", true, false)]
    [InlineData("unknown", true, false)]
    public void Retryable_RequiresRetryableUnderlyingErrorAndSafeExecution(string execution, bool underlying, bool expected)
    {
        PlanStepResult step = Step(0, execution);
        step = step with { Error = new(step.Error?.Reason ?? ErrorReason.EplanBusy, "Stopped.", underlying) };
        PlanResult result = PlanResult.Aggregate([step]);
        Assert.Equal(expected, result.Retryable);
        Assert.Equal(expected, result.Error!.Retryable);
    }

    [Fact]
    public void Aggregate_PartialCommitAndUnknown_NeverRetry_AndUnknownWins()
    {
        PlanResult partial = PlanResult.Aggregate([Step(0, ExecutionState.Committed), Step(1, ExecutionState.RolledBack)],
            new(ErrorReason.EplanBusy, "Stopped.", true));
        Assert.Equal(PlanExecution.PartiallyCommitted, partial.Execution);
        Assert.False(partial.Retryable);
        Assert.False(partial.Error!.Retryable);
        PlanResult unknown = PlanResult.Aggregate([.. partial.Steps, Step(2, ExecutionState.Unknown)], partial.Error);
        Assert.Equal(PlanExecution.Unknown, unknown.Execution);
        Assert.Equal(ErrorReason.OutcomeUnknown, unknown.Error!.Reason);
        Assert.False(unknown.Retryable);
        Assert.False(PlanResult.Aggregate([Step(0, ExecutionState.NotStarted)]).Retryable);
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("committed", ErrorReason.OutcomeUnknown)]
    [InlineData("not_started", ErrorReason.OutcomeUnknown)]
    [InlineData("partially_committed", null)]
    [InlineData("future", null)]
    public void Aggregate_InvalidSingleStepInvariant_Rejects(string execution, string? reason)
    {
        var step = new PlanStepResult(0, "page.update", true, execution, reason is null ? null : new(reason, "Stopped.", false));
        Assert.Throws<ArgumentException>(() => PlanResult.Aggregate([step]));
    }

    [Fact]
    public void Aggregate_UnknownCannotBeInventedWithoutAnEffectiveUnknownStep() =>
        Assert.Throws<ArgumentException>(() => PlanResult.Aggregate([], new(ErrorReason.OutcomeUnknown, "Unknown.", false)));

    private static PlanStepResult Step(int index, string execution) => new(index, "page.update", true, execution,
        execution == ExecutionState.Unknown ? new(ErrorReason.OutcomeUnknown, "Check the project.", false) : null);
}

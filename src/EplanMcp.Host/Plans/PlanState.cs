using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Plans;

internal enum PlanStatus
{
    Compiled, Approved, Running, NotStarted, RolledBack, Committed, PartiallyCommitted, Unknown,
}

/// <summary>A decision summary only, never a reusable approval answer.</summary>
internal sealed record PlanApproval(ApprovalDecision? Decision, string? Channel, string BindingDigest);

/// <summary>Immutable state snapshots. Each mutation returns one new revision; dry-run needs no mutation.</summary>
internal sealed record PlanState(
    long Revision = 0,
    PlanStatus Status = PlanStatus.Compiled,
    string? AttemptId = null,
    string? TaskId = null,
    string? OwnerHost = null,
    PlanApproval? Approval = null,
    PolicyVerdict? Policy = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    PlanResult? Result = null)
{
    public PlanState Admit(string attemptId, string taskId, string ownerHost)
    {
        Require(Status == PlanStatus.Compiled && AttemptId is null);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerHost);
        return this with { Revision = checked(Revision + 1), AttemptId = attemptId, TaskId = taskId, OwnerHost = ownerHost };
    }

    public PlanState Approve(PlanApproval approval)
    {
        Require(Status == PlanStatus.Compiled && AttemptId is not null && approval.Decision == ApprovalDecision.Granted);
        return this with { Revision = checked(Revision + 1), Status = PlanStatus.Approved, Approval = approval, Policy = PolicyVerdict.NeedsApproval };
    }

    public PlanState Start(DateTimeOffset at, PolicyVerdict policy)
    {
        Require(AttemptId is not null && ((Status == PlanStatus.Compiled && policy == PolicyVerdict.Allow)
            || (Status == PlanStatus.Approved && policy == PolicyVerdict.NeedsApproval)));
        return this with { Revision = checked(Revision + 1), Status = PlanStatus.Running, Policy = policy, StartedAt = at };
    }

    public PlanState Complete(PlanResult result, DateTimeOffset at)
    {
        Require(AttemptId is not null && (Status == PlanStatus.Running
            || (Status is PlanStatus.Compiled or PlanStatus.Approved && result.Execution == PlanExecution.NotStarted)));
        Require(StartedAt is null || at >= StartedAt);
        PlanStatus status = result.Execution switch
        {
            PlanExecution.NotStarted => PlanStatus.NotStarted,
            PlanExecution.RolledBack => PlanStatus.RolledBack,
            PlanExecution.Committed => PlanStatus.Committed,
            PlanExecution.PartiallyCommitted => PlanStatus.PartiallyCommitted,
            PlanExecution.Unknown => PlanStatus.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        return this with { Revision = checked(Revision + 1), Status = status, CompletedAt = at, Result = result };
    }

    private static void Require(bool valid)
    {
        if (!valid)
        {
            throw new InvalidOperationException("The plan state transition is not allowed.");
        }
    }
}

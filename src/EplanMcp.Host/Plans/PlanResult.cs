using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

internal sealed record PlanFailure(string Reason, string Message, bool Retryable, JsonElement? Metadata = null);
internal sealed record PlanStepResult(
    int Index, string Op, bool Effective, string Execution, PlanFailure? Error = null,
    string? UndoDescription = null, bool Recovered = false, string? Backup = null);
internal sealed record PlanBackup(string Path, int? Pages, decimal? Seconds, bool Usable);

/// <summary>Execution records write facts independently of validation/diff/coverage (design 4.1 and 4.8).</summary>
internal sealed record PlanResult(
    PlanExecution Execution,
    ImmutableArray<PlanStepResult> Steps,
    PlanFailure? Error,
    ImmutableArray<PlanBackup> Backups,
    string? Verification,
    JsonElement? Validation,
    JsonElement? Diff,
    JsonElement? Coverage)
{
    public bool Retryable => Error is { Retryable: true } && Execution is PlanExecution.NotStarted or PlanExecution.RolledBack;

    /// <summary>Includes not_started results for unsent steps; an empty effective set also represents a dry run.</summary>
    public static PlanResult Aggregate(IEnumerable<PlanStepResult> steps, PlanFailure? error = null)
    {
        ImmutableArray<PlanStepResult> all = [.. steps];
        PlanStepResult[] effective = [.. all.Where(s => s.Effective)];
        foreach (PlanStepResult step in effective)
        {
            if (step.Execution is not (ExecutionState.NotStarted or ExecutionState.RolledBack or ExecutionState.Committed or ExecutionState.Unknown)
                || (step.Execution == ExecutionState.Unknown) != (step.Error?.Reason == ErrorReason.OutcomeUnknown))
            {
                throw new ArgumentException("A step must have a four-value execution consistent with OUTCOME_UNKNOWN.", nameof(steps));
            }
        }

        PlanFailure? unknown = effective.FirstOrDefault(s => s.Error?.Reason == ErrorReason.OutcomeUnknown)?.Error;
        if (unknown is null && error?.Reason == ErrorReason.OutcomeUnknown)
        {
            throw new ArgumentException("OUTCOME_UNKNOWN must come from an effective step.", nameof(error));
        }

        int committed = effective.Count(s => s.Execution == ExecutionState.Committed);
        PlanExecution execution = unknown is not null ? PlanExecution.Unknown
            : effective.Length > 0 && committed == effective.Length ? PlanExecution.Committed
            : committed > 0 ? PlanExecution.PartiallyCommitted
            : effective.Any(s => s.Execution == ExecutionState.RolledBack) ? PlanExecution.RolledBack
            : PlanExecution.NotStarted;
        PlanFailure? failure = unknown ?? error ?? effective.FirstOrDefault(s => s.Error is not null)?.Error;
        if (failure is not null)
        {
            failure = failure with { Retryable = failure.Retryable && execution is PlanExecution.NotStarted or PlanExecution.RolledBack };
        }

        return new(execution, all, failure, [], null, null, null, null);
    }
}

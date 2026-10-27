using System.Text.Json;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Plans;

internal static class PlanStorageRules
{
    public static void Validate(PlanRecord record, string id)
    {
        try
        {
            ValidateCore(record, id);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException or InvalidOperationException)
        {
            throw new JsonException("The plan record contains invalid contract values.", ex);
        }
    }

    private static void ValidateCore(PlanRecord record, string id)
    {
        if (record.SchemaVersion is not (1 or 2) || record.Definition.SchemaVersion != record.SchemaVersion)
        {
            throw new NotSupportedException("Unknown plan schema; keep the record and refuse execution.");
        }
        if (record.Definition.PlanId != PlanPaths.Id(id) || record.PlanHash != PlanCanonical.Hash(record.Definition)
            || record.Definition.ExpiresAt <= record.Definition.CreatedAt || record.State.Revision < 0)
        {
            throw new JsonException("The plan ID, hash, lifetime or revision is invalid.");
        }
        PlanState state = record.State;
        bool terminal = state.Status is not (PlanStatus.Compiled or PlanStatus.Approved or PlanStatus.Running);
        if (terminal != (state.CompletedAt is not null) || terminal != (state.Result is not null)
            || (state.Result is { } result && state.Status.ToString() != result.Execution.ToString())
            || (state.AttemptId is null && state != new PlanState())
            || (state.AttemptId is not null && (state.Revision == 0 || string.IsNullOrWhiteSpace(state.AttemptId) || string.IsNullOrWhiteSpace(state.TaskId) || string.IsNullOrWhiteSpace(state.OwnerHost)))
            || (state.CompletedAt < state.StartedAt)
            || (state.Status == PlanStatus.Approved && (state.Approval?.Decision != ApprovalDecision.Granted || state.Policy != PolicyVerdict.NeedsApproval))
            || (state.Status == PlanStatus.Running && state.Policy is not (PolicyVerdict.Allow or PolicyVerdict.NeedsApproval))
            || (state.Policy == PolicyVerdict.NeedsApproval && state.Approval?.Decision != ApprovalDecision.Granted)
            || (state.Status == PlanStatus.Running && state.StartedAt is null))
        {
            throw new JsonException("The stored plan state is inconsistent.");
        }
        if (state.Result is { } outcome && PlanResult.Aggregate(outcome.Steps, outcome.Error).Execution != outcome.Execution)
        {
            throw new JsonException("The stored result disagrees with its step outcomes.");
        }
    }

    public static void Transition(PlanState before, PlanState after)
    {
        PlanState expected = after.Status switch
        {
            PlanStatus.Compiled when before.AttemptId is null => before.Admit(after.AttemptId!, after.TaskId!, after.OwnerHost!),
            PlanStatus.Approved => before.Approve(after.Approval!),
            PlanStatus.Running => before.Start(after.StartedAt!.Value, after.Policy!.Value),
            _ when after.Result is not null => before.Complete(after.Result, after.CompletedAt!.Value),
            _ => throw new InvalidOperationException("The plan state transition is not allowed."),
        };
        if (expected != after)
        {
            throw new InvalidOperationException("Only one state transition and revision increment may be persisted.");
        }
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

[JsonConverter(typeof(TaskStatusConverter))]
internal enum TaskStatus { Queued, WaitingApproval, Running, Succeeded, Failed, Cancelled, Interrupted, Unknown }
internal sealed class TaskStatusConverter() : JsonStringEnumConverter<TaskStatus>(JsonNamingPolicy.SnakeCaseLower);
internal sealed record TaskProgress(string Message, int CompletedSteps, int TotalSteps);
internal sealed record TaskResultReference(string PlanId, long Revision, string Resource);
internal sealed record TaskError(
    string Reason, string Message, bool Retryable, JsonElement? Metadata,
    ImmutableArray<SystemMessage> EplanMessages, string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Execution = null);

/// <summary>A detached snapshot; execution facts remain in the plan, independently of task status.</summary>
internal sealed record TaskRecord(
    string TaskId, string Kind, string PlanId, TaskStatus Status, TaskProgress Progress,
    TaskResultReference? Result, TaskError? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? ExpiresAt)
{
    [JsonIgnore]
    public bool IsTerminal => Status is not (TaskStatus.Queued or TaskStatus.WaitingApproval or TaskStatus.Running);

    public static TaskStatus StatusFor(PlanResult result, bool cancelled = false, bool interrupted = false) =>
        result.Execution == PlanExecution.Unknown || result.Error?.Reason == ErrorReason.OutcomeUnknown
            || result.Steps.Any(s => s.Effective && s.Error?.Reason == ErrorReason.OutcomeUnknown) ? TaskStatus.Unknown
        : cancelled ? TaskStatus.Cancelled : interrupted ? TaskStatus.Interrupted
        : result.Error is null && result.Steps.All(s => !s.Effective || s.Error is null) && result.Verification == "passed"
            && (result.Execution == PlanExecution.Committed || result.Execution == PlanExecution.NotStarted
                && result.Steps.All(s => !s.Effective)) ? TaskStatus.Succeeded
        : TaskStatus.Failed;
}

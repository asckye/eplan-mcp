using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

internal sealed class PlanCapacityException(string resource, long limit, long used) : Exception("Plan storage is full; wait for retention cleanup or review retained data.")
{
    public string Reason { get; } = ErrorReason.ResourceExhausted;
    public bool Retryable { get; }
    public JsonObject Metadata => new() { ["resource"] = resource, ["limit"] = limit, ["used"] = used };
}

internal static class PlanCapacity
{
    public const int Records = 1000;
    public const long MiB = 1024 * 1024;
    public const long Total = 512 * MiB;
    public const long PerPlan = 16 * MiB;

    public static void Require(string resource, long used, long additional, long limit)
    {
        if (used > limit - additional)
        {
            throw new PlanCapacityException(resource, limit, used);
        }
    }

    public static void Parts(PlanDefinition definition, long recordBytes, long assetBytes)
    {
        long definitionBytes = JsonSerializer.SerializeToUtf8Bytes(definition, PlanJson.Options).LongLength;
        Require("plan.definition", Math.Max(definitionBytes, Encoding.UTF8.GetByteCount(PlanCanonical.Serialize(definition))), 0, 2 * MiB);
        Require("plan.state", recordBytes - definitionBytes, 0, MiB);
        Require("plan.evidence", assetBytes, 0, 13 * MiB);
        Require("plan.bytes", recordBytes + assetBytes, 0, PerPlan);
    }

    /// <summary>Whether the record may still collect state or evidence: admitted, approved, running, or compiled and not expired.</summary>
    public static bool CanGrow(PlanRecord record, DateTimeOffset now) => record.State.Status is PlanStatus.Approved or PlanStatus.Running
        || (record.State.Status == PlanStatus.Compiled && (record.State.AttemptId is not null || now < record.Definition.ExpiresAt));

    public static bool Retained(PlanRecord record, DateTimeOffset now)
    {
        PlanState state = record.State;
        if (state.Status is PlanStatus.Approved or PlanStatus.Running or PlanStatus.Unknown
            || (state.Status == PlanStatus.Compiled && state.AttemptId is not null))
        {
            return true;
        }
        DateTimeOffset end = state.CompletedAt > record.Definition.ExpiresAt ? state.CompletedAt.Value : record.Definition.ExpiresAt;
        return now < end || now - end < TimeSpan.FromDays(30);
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Plans;

internal sealed record PlanArgumentStep(int Index, string Op, JsonElement Args, ToolTier Tier, ImmutableArray<int> DependsOn);
internal sealed record PlanViolation(string Pointer, string Message);
internal sealed record PlanArgumentCheck(ImmutableArray<PlanArgumentStep> Steps, ImmutableArray<PlanViolation> Violations)
{
    public string? Reason => Violations.IsEmpty ? null : ErrorReason.InvalidArgument;
    public JsonObject Metadata => Violations.Length == 1
        ? new() { ["violations"] = JsonSerializer.SerializeToNode(Violations, HostJson.Options), ["pointer"] = Violations[0].Pointer }
        : new() { ["violations"] = JsonSerializer.SerializeToNode(Violations, HostJson.Options) };
}

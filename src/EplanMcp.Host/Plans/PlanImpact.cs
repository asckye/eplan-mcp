using System.Collections.Immutable;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Resolved inputs from the argument compiler; no Bridge session handles or client selectors.</summary>
internal sealed record PlanImpactRequest(string Op, PlanScope Targets, string? DeviceName = null);
internal sealed record PlanOrphan(string Name, ImmutableArray<PlanLocator> Remaining);
internal sealed record PlanImpact(PlanScope ReadScope, PlanScope ModifyScope, ImmutableArray<PlanPrecondition> Preconditions,
    string NamesHash, ImmutableArray<PlanOrphan> Orphaned);
internal sealed record PlanImpactRead(PlanImpact? Impact, ImmutableArray<string> Missing, ImmutableArray<string> Violations,
    ImmutableArray<PlanOrphan> Orphaned)
{
    public string? Reason => !Missing.IsEmpty ? ErrorReason.CapabilityMissing : !Violations.IsEmpty ? ErrorReason.InvalidArgument : null;
}

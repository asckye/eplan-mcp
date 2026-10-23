using System.Collections.Immutable;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Standard evidence only. ComposedHash covers exactly Pages; SnapshotHash requires one whole-project chunk.</summary>
internal sealed record PlanSnapshot(
    PlanProject Project, string Profile, string Format, ImmutableArray<SnapshotPage> Pages,
    string? SnapshotHash, string ComposedHash, ImmutableDictionary<string, string> PageHashes,
    ImmutableDictionary<string, string> Members, string MembersHash, int SkippedConnections);

internal sealed record PlanOccupiedName(string Kind, string Name);
internal sealed record PlanBaseline(PlanSnapshot Snapshot, ImmutableArray<PlanOccupiedName> Names, string NamesHash);
internal sealed record PlanSnapshotConflict(string Scope, string Reason, string? Expected, string? Actual);
internal sealed record PlanBaselineRead(PlanBaseline? Baseline, ImmutableArray<PlanSnapshotConflict> Conflicts)
{
    public string? Reason => Conflicts.IsEmpty ? null : ErrorReason.PlanStale;
}

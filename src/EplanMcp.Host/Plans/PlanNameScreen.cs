using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>Name metadata screens changes without claiming object-property coverage.</summary>
internal static class PlanNameScreen
{
    internal static bool Mismatch(PlanEvidence left, PlanEvidence right, bool between) =>
        (between ? left.NamesHash : left.ExpectedNamesHash) is { } expected && right.NamesHash != expected;

    internal static async Task<ImmutableArray<PlanDiffEntry>> ReadAsync(IBridgeClient bridge, PlanDefinition plan,
        PlanStep step, PlanEvidence left, PlanEvidence right, bool between, CancellationToken cancellationToken)
    {
        if (!Mismatch(left, right, between))
        {
            return [];
        }
        var fresh = await new PlanNamesReader(bridge).ReadAsync(plan.Project.Path, cancellationToken).ConfigureAwait(false);
        if (new PlanProject(fresh.Project, PlanSnapshotHashes.ProjectKey(fresh.ProjectPath), fresh.ProjectGuid) != plan.Project)
        {
            throw PlanApplyChecks.Stale();
        }
        return Compare(step, left, right, fresh.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name)), between);
    }

    internal static ImmutableArray<PlanDiffEntry> Compare(PlanStep step, PlanEvidence left, PlanEvidence right,
        IEnumerable<PlanOccupiedName> fresh, bool between)
    {
        var expected = (between ? left.Names : left.ExpectedNames).ToHashSet();
        var actual = fresh.ToHashSet();
        var observed = right.Names.ToHashSet();
        // Retain the original observation too: a second scan cannot erase a transient alert.
        var changed = expected.SymmetricExcept(right.Names).Union(expected.SymmetricExcept(actual)).Distinct();
        var result = new List<PlanDiffEntry>();
        var oldObjects = PlanVerificationFacts.Objects(left);
        var oldInside = oldObjects.Values.Where(o => PlanScopeRules.Contains(step.ModifyScope,
            PlanScopeRules.Locator(left.Snapshot.Project.Path, o))).Select(o => new PlanOccupiedName(o.Kind, o.Name!)).ToHashSet();
        var newInside = PlanVerificationFacts.Objects(right).Values.Where(o =>
        {
            var locator = PlanScopeRules.Locator(right.Snapshot.Project.Path, o);
            if (oldObjects.TryGetValue(PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), out var prior))
            {
                locator = locator with { Name = prior.Name };
            }
            return PlanScopeRules.Contains(step.ModifyScope, locator);
        }).Select(o => new PlanOccupiedName(o.Kind, o.Name!)).ToHashSet();
        foreach (PlanOccupiedName name in changed.OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Name, StringComparer.Ordinal))
        {
            bool occupied = expected.Contains(name) != observed.Contains(name) ? observed.Contains(name) : actual.Contains(name);
            bool inside = (occupied ? newInside : oldInside).Contains(name);
            inside |= !occupied && name.Kind == "function" && step.Op == "device.update"
                && step.PlannedChanges.Any(c => c.Kind == "device" && c.NewValue == name.Name && oldInside.Contains(new("function", c.Name)));
            inside |= name.Kind == "page" && step.ModifyScope.Locators.IsEmpty
                && PlanScopeRules.Contains(step.ModifyScope, new(left.Snapshot.Project.Path, "page", 0, 0, name.Name, null, null, null));
            string classification = between ? "external_between_steps" : inside ? "unplanned_in_scope" : "outside_scope";
            result.Add(new(step.Index, classification, JsonSerializer.SerializeToElement(new { name.Kind, name.Name, scope = step.ModifyScope }, PlanJson.Options),
                "names", between ? "passed" : "failed", "name_set_mismatch", between ? "external" : "unknown",
                left.Names.Contains(name) ? "occupied" : "absent",
                occupied ? "occupied" : "absent",
                expected.Contains(name) ? "occupied" : "absent"));
        }
        return [.. result];
    }

    private static IEnumerable<PlanOccupiedName> SymmetricExcept(this HashSet<PlanOccupiedName> left, IEnumerable<PlanOccupiedName> right) =>
        left.Except(right).Concat(right.Except(left));
}

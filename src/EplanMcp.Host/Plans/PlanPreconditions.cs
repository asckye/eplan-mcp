using System.Collections.Immutable;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Persistent checks over standard evidence; member hashes detect content changes, additions and deletions.</summary>
internal static class PlanPreconditions
{
    public static ImmutableArray<PlanPrecondition> Build(PlanBaseline baseline, PlanScope scope,
        IEnumerable<PlanOccupiedName>? absent = null, IEnumerable<PlanResource>? resources = null, PlanBinding? binding = null)
    {
        PlanSnapshot snapshot = baseline.Snapshot;
        if (!PlanScopeRules.Missing(baseline, scope).IsEmpty)
        {
            throw new ArgumentException("The read scope is not fully covered by the baseline.", nameof(scope));
        }

        scope = PlanScopeRules.Resolve(snapshot, scope);
        ImmutableArray<SnapshotObject> objects = PlanScopeRules.Select(snapshot, scope);
        var conditions = new List<PlanPrecondition>();
        foreach (SnapshotObject obj in objects)
        {
            PlanLocator locator = PlanScopeRules.Locator(snapshot.Project.Path, obj);
            conditions.Add(new PlanObjectHash(locator, obj.Hash));
            if (obj.Kind == "page")
            {
                conditions.Add(new PlanPageHash(locator, snapshot.PageHashes[PlanSnapshotHashes.Key(obj.Kind, obj.Type, obj.Oid)]));
            }
        }

        conditions.Add(new PlanMembers(scope, PlanSnapshotHashes.Members(snapshot.Project.Path,
            objects.Select(o => new KeyValuePair<string, string>(PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o.Hash))), objects.Length));
        foreach (PlanOccupiedName name in (absent ?? []).Distinct().OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Name, StringComparer.Ordinal))
        {
            if (baseline.Names.Any(n => n.Kind == name.Kind && (string.Equals(n.Name, name.Name, StringComparison.OrdinalIgnoreCase)
                || (name.Kind == "function" && n.Name.StartsWith(name.Name + ":", StringComparison.OrdinalIgnoreCase)))))
            {
                throw new ArgumentException("A requested new name is occupied in the baseline.", nameof(absent));
            }

            conditions.Add(new PlanAbsent(name.Kind, name.Name));
        }

        conditions.AddRange((resources ?? []).Select(r => r.FileHash).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(h => new PlanResourceHash(h)));
        if (binding is not null)
        { conditions.Add(binding); }
        return [.. conditions];
    }
}

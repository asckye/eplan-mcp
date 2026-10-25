using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>Fixed, nonrecursive P20 observation scopes; names remain metadata rather than standard content.</summary>
internal sealed class PlanLocalEvidenceReader(IBridgeClient bridge, PlanStore store)
{
    internal static string[] Pages(PlanStep step, PlanSnapshot known)
    {
        SnapshotObject[] objects = [.. known.Pages.SelectMany(p => p.Objects)];
        PlanLocator[] locators = [.. step.ReadScope.Locators, .. step.ModifyScope.Locators,
            .. step.Preconditions.OfType<PlanObjectHash>().Select(c => c.Locator)];
        var pages = step.ReadScope.Pages.Concat(step.ModifyScope.Pages)
            .Concat(locators.Select(l => l.Kind == "page" ? l.Name : l.Page).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        SnapshotObject[] seeds = [.. objects.Where(o => pages.Contains(o.Kind == "page" ? o.Name! : o.Page!)
            || locators.Any(l => l.Kind == o.Kind && l.Type == o.Type && l.Oid == o.Oid))];
        var names = seeds.Where(o => o.Kind == "function" && !string.IsNullOrEmpty(o.Name)).Select(o => o.Name!)
            .Concat(step.PlannedChanges.Where(c => c.Kind == "device").Select(c => c.Name)).ToHashSet(StringComparer.Ordinal);
        var ends = seeds.SelectMany(o => new[] { o.Start, o.End }).OfType<SnapshotEnd>().Select(e => (e.Type, e.Oid)).ToHashSet();
        pages.UnionWith(objects.Where(o => o.Kind == "function" && (names.Contains(o.Name!) || ends.Contains((o.Type, o.Oid))))
            .Select(o => o.Page).OfType<string>());
        foreach (JsonElement orphan in step.Expected.Where(e => e.GetProperty("tag").GetString() == "orphaned"))
        {
            pages.UnionWith(orphan.GetProperty("value").GetProperty("remaining").EnumerateArray()
                .Select(l => l.GetProperty("page").GetString()).OfType<string>());
        }
        pages.Remove("");
        RequireLimit(pages.Count, 0);
        return [.. pages.Order(StringComparer.Ordinal)];
    }

    public async Task<PlanEvidence> ReadAsync(PlanDefinition plan, PlanStep step, string window, string[] pages,
        PlanEvidence? before, IDictionary<string, string> references, CancellationToken cancellationToken)
    {
        PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(plan.Project.Path, cancellationToken).ConfigureAwait(false);
        if (new PlanProject(names.Project, PlanSnapshotHashes.ProjectKey(names.ProjectPath), names.ProjectGuid) != plan.Project)
        {
            throw PlanApplyChecks.Stale();
        }
        PlanOccupiedName[] occupied = [.. names.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name))];
        string[] pageNames = [.. occupied.Where(n => n.Kind == "page").Select(n => n.Name)];
        RequireLimit(pages.Length, 0);
        string key = ReferenceKey(step.Index, window);
        await PutAsync(key + ":names", new PlanDigests(null, null, names.NamesHash, null)).ConfigureAwait(false);
        await PutAsync(key + ":pageNames", new PlanDigests(null, null,
            PlanSnapshotHashes.Names(plan.Project.Path, occupied.Where(n => n.Kind == "page")), null)).ConfigureAwait(false);

        // Renumber is approved for the complete (at most 50-page) project. Resolve the same page identities after it.
        if (window == "after" && step.Op == "page.renumber" && before is not null)
        {
            ObjectResolveData resolved = await bridge.InvokeAsync<ObjectResolveArgs, ObjectResolveData>(OperationNames.ObjectResolve,
                new()
                {
                    Project = plan.Project.Path,
                    Locators = [.. before.Snapshot.Pages.Select(p => new ObjectLocator
                    {
                        Project = plan.Project.Path,
                        Kind = "page",
                        Type = p.Type,
                        Oid = p.Oid,
                    })],
                }, cancellationToken).ConfigureAwait(false);
            if (resolved.Results.Count != pages.Length || resolved.Results.Any(r => r.Status != "resolved" || r.ActualName is null))
            {
                throw PlanApplyChecks.Stale();
            }
            if (resolved.Project != plan.Project.Name || !Path.IsPathFullyQualified(resolved.ProjectPath)
                || PlanSnapshotHashes.ProjectKey(resolved.ProjectPath) != plan.Project.Path)
            {
                throw PlanApplyChecks.Stale();
            }
            await PutAsync(key + ":pages", resolved).ConfigureAwait(false);
            pages = [.. resolved.Results.Select(r => r.ActualName!).Order(StringComparer.Ordinal)];
        }
        string[] missing = [.. pages.Except(pageNames, StringComparer.Ordinal)];
        string action = window == "before" && step.Op == "page.create" ? "create"
            : window == "after" && step.Op == "page.delete" ? "delete" : "";
        string[] approved = [.. step.PlannedChanges.Where(c => c.Kind == "page" && c.Action == action).Select(c => c.Name)];
        if (missing.Except(approved, StringComparer.Ordinal).Any())
        {
            throw PlanApplyChecks.Stale();
        }
        var conflicts = new List<PlanSnapshotConflict>();
        var read = new List<SnapshotPage>();
        int skipped = 0;
        foreach (string[] batch in pages.Except(missing, StringComparer.Ordinal).Chunk(1000))
        {
            PlanSnapshot? part = await new PlanBaselineReader(bridge).AssembleAsync(plan.Project, batch, conflicts, cancellationToken).ConfigureAwait(false);
            if (part is null)
            {
                throw PlanApplyChecks.Stale();
            }
            read.AddRange(part.Pages);
            skipped = Math.Max(skipped, part.SkippedConnections);
            RequireLimit(read.Count, read.Sum(p => p.Objects.Count));
        }
        if (window == "after" && step.Op == "page.renumber" && before is not null
            && !before.Snapshot.Pages.Select(p => (p.Type, p.Oid)).ToHashSet().SetEquals(read.Select(p => (p.Type, p.Oid))))
        {
            throw PlanApplyChecks.Stale();
        }
        var members = read.SelectMany(p => p.Objects).ToImmutableDictionary(o => PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o => o.Hash, StringComparer.Ordinal);
        var snapshot = new PlanSnapshot(plan.Project, SnapshotProfiles.StandardName, SnapshotProfiles.Format, [.. read], null,
            PlanSnapshotHashes.Compose(read), read.ToImmutableDictionary(p => PlanSnapshotHashes.Key("page", p.Type, p.Oid), p => p.Hash, StringComparer.Ordinal),
            members, PlanSnapshotHashes.Members(plan.Project.Path, members), skipped);
        var observation = new PlanEvidence(snapshot)
        {
            AbsentPages = [.. missing],
            Names = [.. occupied],
            NamesHash = names.NamesHash,
        };
        await PutAsync(key, observation).ConfigureAwait(false);
        if (window == "before" && step.Op is not ("page.renumber" or "device.renumber"))
        {
            PlanOccupiedName[] expected = ExpectedNames(step, occupied, snapshot);
            string expectedHash = PlanSnapshotHashes.Names(plan.Project.Path, expected);
            observation = observation with { ExpectedNames = [.. expected], ExpectedNamesHash = expectedHash };
            await PutAsync(key + ":expectedNames", new PlanDigests(null, null, expectedHash, null)).ConfigureAwait(false);
        }
        return observation;

        async Task PutAsync<T>(string reference, T value)
        {
            references[reference] = await store.PutEvidenceAsync(plan.PlanId,
                JsonSerializer.SerializeToUtf8Bytes(value, PlanJson.Options), CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal static string ReferenceKey(int step, string window) => step.ToString(CultureInfo.InvariantCulture) + ":" + window;

    private static void RequireLimit(int pages, int objects)
    {
        if (pages > 50 || objects > 10_000)
        {
            throw PlanLocalScopeRules.Missing("plan.evidence:50_pages_10000_objects");
        }
    }

    internal static PlanOccupiedName[] ExpectedNames(PlanStep step, IEnumerable<PlanOccupiedName> names, PlanSnapshot before)
    {
        var expected = names.ToHashSet();
        SnapshotObject[] functions = [.. before.Pages.SelectMany(p => p.Objects).Where(o => o.Kind == "function")];
        foreach (PlanChange change in step.PlannedChanges)
        {
            if (change.Kind == "page" && change.Action == "create")
            {
                expected.Add(new("page", change.Name));
            }
            if (change.Kind == "page" && change.Action == "delete")
            {
                expected.Remove(new("page", change.Name));
            }
            if (step.Op == "device.update" && change.Kind == "device" && change.NewValue is { } renamed)
            {
                SnapshotObject[] old = [.. functions.Where(f => f.Name == change.Name)];
                if (old.Length > 0 && old.All(f => PlanScopeRules.Contains(step.ModifyScope, PlanScopeRules.Locator(before.Project.Path, f))))
                {
                    expected.Remove(new("function", change.Name));
                }
                expected.Add(new("function", renamed));
            }
        }
        if (step.Op == "page.delete")
        {
            var deleted = step.PlannedChanges.Where(c => c.Kind == "page" && c.Action == "delete").Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var group in functions.Where(f => !string.IsNullOrEmpty(f.Name)).GroupBy(f => f.Name!, StringComparer.Ordinal))
            {
                if (group.All(f => deleted.Contains(f.Page!)))
                {
                    expected.Remove(new("function", group.Key));
                }
            }
        }
        if (step.Op == "macro.insert")
        {
            expected.UnionWith(step.Preconditions.OfType<PlanAbsent>().Where(c => c.Kind == "function").Select(c => new PlanOccupiedName(c.Kind, c.Name)));
        }
        return [.. expected.OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Name, StringComparer.Ordinal)];
    }
}

using System.Collections.Immutable;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>Read-only impact closure. Incomplete evidence is refused, never narrowed to the visible functions (A13/A18).</summary>
internal sealed class PlanImpactReader(IBridgeClient bridge)
{
    public async Task<PlanImpactRead> ReadAsync(PlanBaseline baseline, PlanImpactRequest request, CancellationToken cancellationToken)
    {
        PlanSnapshot snapshot = baseline.Snapshot;
        string op = request.Op;
        if (op is not ("page.create" or "page.update" or "page.delete" or "page.renumber" or "device.update" or "device.renumber" or "macro.insert"))
        {
            return new(null, [], ["/op"], []);
        }

        ImmutableArray<string> missing = op == "page.create" ? [] : PlanScopeRules.Missing(baseline, request.Targets);
        if (!missing.IsEmpty)
        { return new(null, missing, [], []); }
        string[] pages = op == "page.create" ? [.. request.Targets.Pages]
            : [.. PlanScopeRules.Select(snapshot, request.Targets).Select(o => o.Kind == "page" ? o.Name : o.Page).OfType<string>().Distinct(StringComparer.Ordinal)];
        if ((pages.Length is < 1 or > 50 && op != "device.update") || (op == "device.update" && string.IsNullOrEmpty(request.DeviceName)))
        {
            return new(null, [], ["/scope/pages", "/deviceName"], []);
        }

        var objects = snapshot.Pages.SelectMany(p => p.Objects).ToArray();
        string[] names = op == "device.update" ? [request.DeviceName!]
            : op == "page.delete" ? [.. objects.Where(o => o.Kind == "function" && pages.Contains(o.Page, StringComparer.Ordinal))
                .Select(o => o.Name).OfType<string>().Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)] : [];
        PlanFunctionsData? functions = null;
        if (names.Length > 0)
        {
            functions = new() { Project = snapshot.Project.Name, ProjectPath = snapshot.Project.Path, Complete = true, Limit = 10_000 };
            foreach (string[] chunk in names.Chunk(200))
            {
                PlanFunctionsData part = await bridge.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
                    new() { Project = snapshot.Project.Path, Names = [.. chunk], Pages = op == "page.delete" ? [.. pages] : null }, cancellationToken).ConfigureAwait(false);
                functions.Complete &= part.Complete && part.Limit == 10_000 && part.Project == snapshot.Project.Name
                    && Path.IsPathFullyQualified(part.ProjectPath) && PlanSnapshotHashes.ProjectKey(part.ProjectPath) == PlanSnapshotHashes.ProjectKey(snapshot.Project.Path);
                functions.Functions.AddRange(part.Functions);
            }

            functions.Functions = [.. functions.Functions.DistinctBy(f => (f.Kind, f.Type, f.Oid, f.Name, f.IsMain, f.Page))
                .OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Page, StringComparer.Ordinal).ThenBy(f => f.Type).ThenBy(f => f.Oid)];
        }

        return Close(baseline, request, pages, names, functions);
    }

    private static PlanImpactRead Close(PlanBaseline baseline, PlanImpactRequest request, string[] pages, string[] names, PlanFunctionsData? data)
    {
        PlanSnapshot snapshot = baseline.Snapshot;
        string project = snapshot.Project.Path, op = request.Op;
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<string>();
        SnapshotObject[] objects = [.. snapshot.Pages.SelectMany(p => p.Objects)];
        var byKey = objects.ToDictionary(o => PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), StringComparer.Ordinal);
        PlanFunction[] functions = data is null ? [] : [.. data.Functions];
        if (data is not null)
        {
            if (!data.Complete || data.Functions.Count > 10_000)
            { missing.Add("plan.functions:complete"); }
            if (!Path.IsPathFullyQualified(data.ProjectPath) || PlanSnapshotHashes.ProjectKey(data.ProjectPath) != PlanSnapshotHashes.ProjectKey(project)
                || data.Project != snapshot.Project.Name)
            { missing.Add("plan.functions:project"); }
            foreach (PlanFunction f in functions)
            {
                if (f.Page is null)
                { missing.Add("snapshot:functions_without_page"); }
                if (f.Kind != "function" || !byKey.TryGetValue(PlanSnapshotHashes.Key(f.Kind, f.Type, f.Oid), out SnapshotObject? obj)
                    || obj.Name != f.Name || obj.Page != f.Page || obj.Main != f.IsMain)
                { missing.Add("snapshot:functions"); }
                if (!names.Contains(f.Name, StringComparer.Ordinal) && !(op == "page.delete" && pages.Contains(f.Page, StringComparer.Ordinal)))
                { missing.Add("plan.functions:selection"); }
            }

            var keys = functions.Select(f => PlanSnapshotHashes.Key(f.Kind, f.Type, f.Oid)).ToHashSet(StringComparer.Ordinal);
            if (keys.Count != functions.Length || objects.Any(o => o.Kind == "function"
                && (names.Contains(o.Name, StringComparer.Ordinal) || (op == "page.delete" && pages.Contains(o.Page, StringComparer.Ordinal)))
                && !keys.Contains(PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid))))
            { missing.Add("plan.functions:membership"); }
        }

        ImmutableArray<PlanOrphan> orphans = op != "page.delete" || data?.Complete != true ? [] : [.. functions
            .Where(f => f.IsMain && f.Name.Length > 0 && pages.Contains(f.Page, StringComparer.Ordinal)).Select(f => f.Name).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(name => (Name: name, Left: functions.Where(f => f.Name == name && !pages.Contains(f.Page, StringComparer.Ordinal)).ToArray()))
            .Where(g => g.Left.Length > 0 && !g.Left.Any(f => f.IsMain)).Select(g => new PlanOrphan(g.Name,
                [.. g.Left.Select(f => FunctionLocator(project, objects, f))]))];
        PlanScope modify = PlanScopeRules.Pages(pages);
        if (op == "device.update")
        {
            if (functions.Length == 0 || !functions.Any(f => f.IsMain))
            { missing.Add("plan.functions:device"); }
            modify = new([.. functions.Select(f => f.Page).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], [],
                [.. functions.Select(f => FunctionLocator(project, objects, f))]);
        }
        else if (op == "page.update")
        {
            modify = modify with { Locators = [.. objects.Where(o => o.Kind == "page" && pages.Contains(o.Name, StringComparer.Ordinal)).Select(o => PlanScopeRules.Locator(project, o))] };
        }
        else if (op == "page.delete")
        {
            PlanLocator[] remaining = [.. orphans.SelectMany(o => o.Remaining)];
            modify = new([.. pages.Concat(remaining.Select(l => l.Page).OfType<string>()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], [],
                [.. objects.Where(o => pages.Contains(o.Kind == "page" ? o.Name : o.Page, StringComparer.Ordinal)).Select(o => PlanScopeRules.Locator(project, o)).Concat(remaining)]);
        }

        bool renumber = op is "page.renumber" or "device.renumber";
        if (renumber)
        { modify = PlanScopeRules.Whole(snapshot); }
        // Collision domains are read conservatively in full; their name-set digest is retained separately from coverage.
        PlanScope read = op is "page.create" or "device.update" or "macro.insert" || renumber ? PlanScopeRules.Whole(snapshot)
            : PlanScopeRules.Pages(pages.Concat(functions.Select(f => f.Page).OfType<string>()));
        if (op is "page.create" or "device.update" or "macro.insert" || renumber)
        {
            if (baseline.Names.Any(n => n.Kind == "page" && !snapshot.Pages.Any(p => p.Name == n.Name)))
            { missing.Add("snapshot:project_pages"); }
        }

        if (modify.Pages.Length > 50)
        { violations.Add("/modifyScope/pages"); }
        if (PlanScopeRules.Select(snapshot, read).Length > 10_000)
        { violations.Add("/readScope/objects"); }
        if (missing.Count > 0 || violations.Count > 0)
        { return new(null, [.. missing.Order(StringComparer.Ordinal)], [.. violations], orphans); }
        ImmutableArray<PlanPrecondition> preconditions = PlanPreconditions.Build(baseline, read,
            op == "page.create" ? pages.Select(p => new PlanOccupiedName("page", p)) : null);
        return new(new(read, modify, preconditions, baseline.NamesHash, orphans), [], [], orphans);
    }

    private static PlanLocator FunctionLocator(string project, SnapshotObject[] objects, PlanFunction f) =>
        objects.FirstOrDefault(o => o.Kind == f.Kind && o.Type == f.Type && o.Oid == f.Oid) is { } obj ? PlanScopeRules.Locator(project, obj)
            : new(PlanSnapshotHashes.ProjectKey(project), f.Kind, f.Type, f.Oid, f.Name, f.Page, null, null);
}

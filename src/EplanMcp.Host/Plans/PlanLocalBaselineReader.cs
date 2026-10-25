using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>Version 2 discovery and two local reads; no retries or caching.</summary>
internal sealed class PlanLocalBaselineReader(IBridgeClient bridge)
{
    public Task<PlanBaselineRead> ReadAsync(string project, IReadOnlyList<PlanImpactRequest> requests, CancellationToken cancellationToken) =>
        ReadAsync(project, requests, (_, _) => Task.CompletedTask, cancellationToken);

    public async Task<PlanBaselineRead> ReadAsync(string project, IReadOnlyList<PlanImpactRequest> requests,
        Func<PlanBaseline, IReadOnlyList<PlanLocalImpact>, Task> compile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectListData listed = await bridge.InvokeAsync<object, ProjectListData>(OperationNames.ProjectList, new(), cancellationToken).ConfigureAwait(false);
        PlanNamesData inventory = await new PlanNamesReader(bridge).ReadAsync(project, cancellationToken).ConfigureAwait(false);
        var identity = new PlanProject(inventory.Project, PlanSnapshotHashes.ProjectKey(inventory.ProjectPath), inventory.ProjectGuid);
        if (!listed.Projects.Any(p => p.Name == identity.Name && Path.IsPathFullyQualified(p.Path)
            && PlanSnapshotHashes.ProjectKey(p.Path) == identity.Path))
        {
            throw PlanLocalScopeRules.Stale("The open project identity changed during discovery; compile again.");
        }
        PlanLocalImpact[] discovered = await DiscoverAsync(identity, inventory, requests, cancellationToken).ConfigureAwait(false);
        var created = requests.Where(r => r.Op == "page.create").SelectMany(r => r.Targets.Pages)
            .Except(inventory.Names.Where(n => n.Kind == "page").Select(n => n.Name), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        string[] pages = [.. discovered.SelectMany(i => i.ReadScope.Pages).Where(p => !created.Contains(p))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        var conflicts = new List<PlanSnapshotConflict>();
        var first = await ReadOnceAsync(null).ConfigureAwait(false);
        if (first.Baseline is null)
        {
            return new(null, [.. conflicts]);
        }
        await compile(first.Baseline, first.Impacts).ConfigureAwait(false);
        var second = await ReadOnceAsync(first.Impacts).ConfigureAwait(false);
        if (second.Baseline is not null)
        {
            Check("pages", first.Baseline.Snapshot.PageHashes.OrderBy(p => p.Key, StringComparer.Ordinal), second.Baseline.Snapshot.PageHashes.OrderBy(p => p.Key, StringComparer.Ordinal));
            Check("members", first.Baseline.Snapshot.Members.OrderBy(p => p.Key, StringComparer.Ordinal), second.Baseline.Snapshot.Members.OrderBy(p => p.Key, StringComparer.Ordinal));
            Check("names", first.Baseline.NamesHash, second.Baseline.NamesHash);
            Check("skippedConnections", first.Baseline.Snapshot.SkippedConnections, second.Baseline.Snapshot.SkippedConnections);
        }
        return new(conflicts.Count == 0 ? first.Baseline : null, [.. conflicts]);

        async Task<(PlanBaseline? Baseline, PlanLocalImpact[] Impacts)> ReadOnceAsync(PlanLocalImpact[]? expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlanSnapshot? snapshot = await ReadSnapshotAsync(identity, pages, conflicts, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return (null, []);
            }
            PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(identity.Path, cancellationToken).ConfigureAwait(false);
            Check("project", identity, new PlanProject(names.Project, PlanSnapshotHashes.ProjectKey(names.ProjectPath), names.ProjectGuid));
            if (requests.Any(r => r.Op is "page.renumber" or "device.renumber")
                && names.Names.Where(n => n.Kind == "page").Select(n => n.Name).Except(pages, StringComparer.Ordinal).Any())
            {
                conflicts.Add(new("pages", "expanded", null, null));
                return (null, []);
            }
            PlanLocalImpact[] impacts = await DiscoverAsync(identity, names, requests, cancellationToken, expected).ConfigureAwait(false);
            if (impacts.SelectMany(i => i.ReadScope.Pages).Except(pages.Concat(created), StringComparer.Ordinal).Any())
            {
                conflicts.Add(new("closure", "expanded", null, null));
            }
            var baseline = new PlanBaseline(snapshot, [.. names.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name))], names.NamesHash!);
            if (conflicts.Count == 0)
            {
                foreach (PlanLocalImpact impact in impacts.Where(i => !i.ReadScope.Pages.Any(created.Contains)))
                {
                    PlanLocalScopeRules.Validate(baseline, impact);
                }
            }
            return (conflicts.Count == 0 ? baseline : null, impacts);
        }

        void Check<T>(string scope, T expected, T actual)
        {
            string before = JsonSerializer.Serialize(expected);
            string after = JsonSerializer.Serialize(actual);
            if (before != after)
            {
                conflicts.Add(new(scope, "changed", before, after));
            }
        }
    }

    /// <summary>Apply reads the approved S, never rediscovering or expanding its content scope.</summary>
    internal async Task<PlanBaselineRead> RecheckAsync(PlanBaseline expected, IReadOnlyList<PlanStep> steps, CancellationToken cancellationToken)
    {
        PlanProject project = expected.Snapshot.Project;
        PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(project.Path, cancellationToken).ConfigureAwait(false);
        if (new PlanProject(names.Project, PlanSnapshotHashes.ProjectKey(names.ProjectPath), names.ProjectGuid) != project)
        {
            throw PlanApplyChecks.Stale();
        }
        bool renumber = steps.Any(s => s.Op is "page.renumber" or "device.renumber");
        int pageCount = names.Names.Count(n => n.Kind == "page");
        if (renumber)
        {
            PlanLocalScopeRules.RenumberLimit(pageCount, expected.Snapshot.Members.Count);
        }
        if (names.NamesHash != expected.NamesHash)
        {
            throw PlanApplyChecks.Stale();
        }

        var closures = new List<PlanLocalImpact>();
        var objects = expected.Snapshot.Pages.SelectMany(p => p.Objects).ToDictionary(o => (o.Kind, o.Type, o.Oid));
        foreach (PlanStep step in steps.Where(s => s.Op == "device.update"))
        {
            var functions = new List<PlanFunction>();
            foreach (PlanLocator locator in step.ModifyScope.Locators)
            {
                if (locator.Page is null or "")
                {
                    throw PlanLocalScopeRules.Missing("object.resolve:functions_without_page");
                }
                if (!objects.TryGetValue((locator.Kind, locator.Type, locator.Oid), out SnapshotObject? obj)
                    || obj.Name != locator.Name || obj.Page != locator.Page || obj.Main is null)
                {
                    throw PlanApplyChecks.Stale();
                }
                functions.Add(new()
                {
                    Kind = obj.Kind,
                    Type = obj.Type,
                    Oid = obj.Oid,
                    Name = obj.Name!,
                    Page = obj.Page,
                    IsMain = obj.Main.Value,
                });
            }
            string name = functions.Select(f => f.Name).Distinct(StringComparer.Ordinal).SingleOrDefault() ?? throw PlanApplyChecks.Stale();
            closures.Add(new(new(step.Op, step.ReadScope, name), step.ReadScope, step.ModifyScope, [.. functions]));
        }
        _ = await DiscoverAsync(project, names, closures.Select(i => i.Request).ToArray(), cancellationToken, [.. closures]).ConfigureAwait(false);
        var conflicts = new List<PlanSnapshotConflict>();
        PlanSnapshot? snapshot = await ReadSnapshotAsync(project, expected.Snapshot.Pages.Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(), conflicts, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new(null, [.. conflicts]);
        }
        if (renumber)
        {
            PlanLocalScopeRules.RenumberLimit(pageCount, snapshot.Members.Count);
        }
        if (snapshot.SkippedConnections != expected.Snapshot.SkippedConnections)
        {
            throw PlanApplyChecks.Stale();
        }
        return new(new(snapshot, [.. names.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name))], names.NamesHash!), []);
    }

    private async Task<PlanSnapshot?> ReadSnapshotAsync(PlanProject project, string[] pages, List<PlanSnapshotConflict> conflicts, CancellationToken cancellationToken)
    {
        var readPages = new List<SnapshotPage>();
        int skipped = 0;
        foreach (string[] batch in pages.Chunk(1000))
        {
            PlanSnapshot? part = await new PlanBaselineReader(bridge).AssembleAsync(project, batch, conflicts, cancellationToken).ConfigureAwait(false);
            if (part is null)
            {
                return null;
            }
            readPages.AddRange(part.Pages);
            skipped = Math.Max(skipped, part.SkippedConnections);
        }
        var members = readPages.SelectMany(p => p.Objects).ToImmutableDictionary(o => PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o => o.Hash, StringComparer.Ordinal);
        return new(project, SnapshotProfiles.StandardName, SnapshotProfiles.Format, [.. readPages], null,
            PlanSnapshotHashes.Compose(readPages), readPages.ToImmutableDictionary(p => PlanSnapshotHashes.Key("page", p.Type, p.Oid), p => p.Hash, StringComparer.Ordinal),
            members, PlanSnapshotHashes.Members(project.Path, members), skipped);
    }

    private async Task<PlanLocalImpact[]> DiscoverAsync(PlanProject project, PlanNamesData names, IReadOnlyList<PlanImpactRequest> requests,
        CancellationToken cancellationToken, PlanLocalImpact[]? expected = null)
    {
        var result = new List<PlanLocalImpact>();
        foreach (PlanImpactRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlanImpactRequest resolved = request with
            {
                Targets = PlanScopeRules.Pages(request.Targets.Pages.Select(p => request.Op == "page.create" ? p
                    : names.Names.FirstOrDefault(n => n.Kind == "page" && string.Equals(n.Name, p, StringComparison.OrdinalIgnoreCase))?.Name ?? p)),
            };
            PlanFunctionsData? functions = null;
            string[] selected = request.Op == "device.update" ? [request.DeviceName!] : [];
            if (request.Op == "page.delete")
            {
                var gaps = new List<PlanSnapshotConflict>();
                PlanSnapshot snapshot = await ReadSnapshotAsync(project, [.. resolved.Targets.Pages], gaps, cancellationToken).ConfigureAwait(false)
                    ?? throw PlanLocalScopeRules.Stale("A deletion page is missing; compile again.");
                selected = [.. snapshot.Pages.SelectMany(p => p.Objects).Where(o => o.Kind == "function")
                    .Select(o => o.Name).OfType<string>().Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)];
            }
            foreach (string[] batch in selected.Chunk(200))
            {
                PlanFunctionsData part = await bridge.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
                    new()
                    {
                        Project = project.Path,
                        Names = [.. batch],
                        Pages = request.Op == "page.delete" ? [.. resolved.Targets.Pages] : null,
                    }, cancellationToken).ConfigureAwait(false);
                bool wrongProject = part.Project != project.Name || !Path.IsPathFullyQualified(part.ProjectPath)
                    || PlanSnapshotHashes.ProjectKey(part.ProjectPath) != project.Path;
                if (!part.Complete || part.Limit != 10_000 || part.Functions.Count > 10_000
                    || wrongProject)
                {
                    throw expected is not null && wrongProject
                        ? PlanLocalScopeRules.Stale("The function inventory project changed between reads; compile again.")
                        : PlanLocalScopeRules.Missing("plan.functions:complete");
                }
                if (request.Op == "device.update")
                {
                    functions = part;
                }
                else
                {
                    functions ??= new();
                    functions.Functions = [.. functions.Functions.Concat(part.Functions).DistinctBy(f => (f.Kind, f.Type, f.Oid, f.Name, f.IsMain, f.Page))];
                }
            }
            if (expected is not null && JsonSerializer.Serialize(Sorted(functions?.Functions ?? []))
                != JsonSerializer.Serialize(Sorted(expected[result.Count].Functions)))
            {
                throw PlanLocalScopeRules.Stale("The same-name function closure changed between reads; compile again.");
            }
            PlanLocalImpact impact = PlanLocalScopeRules.Resolve(project, resolved, names.Names.Where(n => n.Kind == "page").Select(n => n.Name), functions?.Functions);
            if (impact.Functions.Any(f => f.Page is null or ""))
            {
                throw PlanLocalScopeRules.Missing("object.resolve:functions_without_page");
            }
            result.Add(impact);
        }
        return [.. result];
    }

    private static PlanFunction[] Sorted(IEnumerable<PlanFunction> functions) => [.. functions
        .OrderBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.Type).ThenBy(f => f.Oid)];
}

using System.Collections.Immutable;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>
/// Reads resolved page scopes and project-wide names (including unplaced functions) twice. No cache or retry: this screens
/// stability, not atomicity (S31). Scope resolution/impact closure belongs to the compiler, not this reader.
/// </summary>
internal sealed class PlanBaselineReader(IBridgeClient bridge)
{
    /// <summary>Fresh context evidence, without the compiler's double read or project-wide name domain.</summary>
    public async Task<PlanSnapshot> SnapshotAsync(string? project, string[]? pages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectSnapshotData first = await bridge.InvokeAsync<ProjectSnapshotArgs, ProjectSnapshotData>(OperationNames.ProjectSnapshot,
            new() { Project = project, Pages = pages?.ToList(), Profile = SnapshotProfiles.StandardName }, cancellationToken).ConfigureAwait(false);
        if (!Path.IsPathFullyQualified(first.ProjectPath))
        {
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.CapabilityMissing,
                Message = "Snapshot project identity is unavailable. Read again with a compatible Bridge.",
                Metadata = new() { ["missing"] = "snapshot:project_path" },
            });
        }

        var conflicts = new List<PlanSnapshotConflict>();
        return await AssembleAsync(new(first.Project, first.ProjectPath, first.ProjectGuid), pages, conflicts, cancellationToken, first).ConfigureAwait(false)
            ?? throw new BridgeCallException(new BridgeError
            {
                Reason = conflicts.Any(c => c.Reason == "missing") ? ErrorReason.ResourceNotFound : ErrorReason.CapabilityMissing,
                Message = "Snapshot coverage is incomplete or inconsistent. Narrow the scope and read again.",
                Metadata = new() { ["missing"] = string.Join("\n", conflicts.Select(c => c.Scope + ":" + c.Reason)) },
            });
    }

    public Task<PlanBaselineRead> ReadAsync(PlanProject project, IReadOnlyList<string>? pages, CancellationToken cancellationToken) =>
        ReadAsync(project, pages, _ => Task.CompletedTask, cancellationToken);

    public async Task<PlanBaselineRead> ReadAsync(PlanProject project, IReadOnlyList<string>? pages,
        Func<PlanBaseline, Task> compile, CancellationToken cancellationToken)
    {
        string[]? wanted = pages?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (wanted is { Length: 0 })
        {
            throw new ArgumentException("Use null for all pages, or supply resolved page names.", nameof(pages));
        }

        var conflicts = new List<PlanSnapshotConflict>();
        PlanBaseline? first = await ReadOnceAsync(project, wanted, conflicts, cancellationToken).ConfigureAwait(false);
        if (first is null)
        {
            return new(null, [.. conflicts]);
        }

        await compile(first).ConfigureAwait(false);
        PlanBaseline? second = await ReadOnceAsync(project, wanted, conflicts, cancellationToken).ConfigureAwait(false);
        if (second is not null)
        {
            Compare("pages", first.Snapshot.PageHashes, second.Snapshot.PageHashes, conflicts);
            Compare("members", first.Snapshot.Members, second.Snapshot.Members, conflicts);
            Check("names", first.NamesHash, second.NamesHash, conflicts);
            Check("skippedConnections", first.Snapshot.SkippedConnections, second.Snapshot.SkippedConnections, conflicts);
        }

        return new(conflicts.Count == 0 ? first : null, [.. conflicts]);
    }

    private async Task<PlanBaseline?> ReadOnceAsync(PlanProject project, string[]? pages, List<PlanSnapshotConflict> conflicts, CancellationToken cancellationToken)
    {
        PlanSnapshot? scope = await AssembleAsync(project, pages, conflicts, cancellationToken).ConfigureAwait(false);
        if (scope is null)
        {
            return null;
        }

        // Page placements also include extra representations; neither a local scope nor standard alone proves absence.
        PlanSnapshot? names = pages is null ? scope
            : await AssembleAsync(project, null, conflicts, cancellationToken).ConfigureAwait(false);
        if (names is null)
        {
            return null;
        }

        var occupiedNames = names.Pages.SelectMany(p => p.Objects)
            .Where(o => o.Kind is "page" or "function" && o.Name is not null)
            .Select(o => new PlanOccupiedName(o.Kind, o.Name!)).ToList();
        int offset = 0;
        int? total = null;
        do
        {
            ListData<DeviceListItem> part = await bridge.InvokeAsync<DeviceListArgs, ListData<DeviceListItem>>(OperationNames.DeviceList,
                new() { Project = project.Path, IncludeAuxiliary = true, Offset = offset, Limit = 500 }, cancellationToken).ConfigureAwait(false);
            Check("names:project", project.Name, part.Project, conflicts);
            Check("names:total", total ?? part.Total, part.Total, conflicts);
            Check("names:slice", Math.Min(500, part.Total - offset), part.Items.Count, conflicts);
            if (part.Total < 0 || conflicts.Count > 0)
            {
                return null;
            }
            occupiedNames.AddRange(part.Items.Select(f => new PlanOccupiedName("function", f.Tag)));
            total = part.Total;
            offset += part.Items.Count;
        } while (offset < total);
        ImmutableArray<PlanOccupiedName> occupied = [.. occupiedNames.Distinct()
            .OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Name, StringComparer.Ordinal)];
        return new(scope, occupied, PlanSnapshotHashes.Names(project.Path, occupied));
    }

    internal async Task<PlanSnapshot?> AssembleAsync(PlanProject project, string[]? wanted, List<PlanSnapshotConflict> conflicts, CancellationToken cancellationToken, ProjectSnapshotData? initial = null)
    {
        List<SnapshotPage> pages = [];
        var members = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        long? after = null;
        int? count = null;
        int? skipped = null;
        string? wholeHash = null;
        int chunks = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectSnapshotData chunk = initial ?? await bridge.InvokeAsync<ProjectSnapshotArgs, ProjectSnapshotData>(OperationNames.ProjectSnapshot,
                new() { Project = project.Path, Pages = wanted?.ToList(), AfterPage = after, Profile = SnapshotProfiles.StandardName }, cancellationToken).ConfigureAwait(false);
            initial = null;
            chunks++;
            string? actualPath = Path.IsPathFullyQualified(chunk.ProjectPath) ? PlanSnapshotHashes.ProjectKey(chunk.ProjectPath) : null;
            Check("projectPath", PlanSnapshotHashes.ProjectKey(project.Path), actualPath, conflicts);
            Check("projectGuid", project.Guid, chunk.ProjectGuid, conflicts);
            Check("project", project.Name, chunk.Project, conflicts);
            Check("profile", SnapshotProfiles.StandardName, chunk.Profile, conflicts);
            Check("format", SnapshotProfiles.Format, chunk.Format, conflicts);
            Check("pageCount", count ?? chunk.PageCount, chunk.PageCount, conflicts);
            Check("skippedConnections", skipped ?? chunk.SkippedConnections, chunk.SkippedConnections, conflicts);
            count = chunk.PageCount;
            skipped = chunk.SkippedConnections;
            foreach (string missing in chunk.MissingPages)
            {
                conflicts.Add(new("pages", "missing", missing, null));
            }

            foreach (SnapshotPage page in chunk.Pages)
            {
                if (page.Oid <= (after ?? 0) || !names.Add(page.Name) || (wanted is not null && !wanted.Contains(page.Name, StringComparer.Ordinal)))
                {
                    conflicts.Add(new("pages", "invalid_page_list", null, page.Name));
                }

                after = page.Oid;
                if (page.Objects.Count(o => o.Kind == "page" && o.Type == page.Type && o.Oid == page.Oid && o.Name == page.Name) != 1)
                {
                    conflicts.Add(new("pages", "missing_page_object", page.Name, null));
                }

                Check("pageHash:" + PlanSnapshotHashes.Key("page", page.Type, page.Oid), page.Hash, PlanSnapshotHashes.Page(page.Objects), conflicts);
                foreach (SnapshotObject obj in page.Objects)
                {
                    if (!members.TryAdd(PlanSnapshotHashes.Key(obj.Kind, obj.Type, obj.Oid), obj.Hash))
                    {
                        conflicts.Add(new("members", "duplicate", null, PlanSnapshotHashes.Key(obj.Kind, obj.Type, obj.Oid)));
                    }
                }

                pages.Add(page);
            }

            bool whole = chunks == 1 && wanted is null && chunk.Complete;
            Check("snapshotHash", whole ? PlanSnapshotHashes.Compose(pages) : null, chunk.SnapshotHash, conflicts);
            if (whole)
            {
                wholeHash = chunk.SnapshotHash;
            }

            if (chunk.Complete)
            {
                Check("nextAfterPage", (long?)null, chunk.NextAfterPage, conflicts);
                break;
            }

            if (chunk.Pages.Count == 0 || chunk.NextAfterPage != after)
            {
                conflicts.Add(new("pages", "invalid_continuation", null, null));
            }

            if (conflicts.Count > 0)
            {
                return null;
            }
        }

        Check("pageCount", count, (int?)pages.Count, conflicts);
        if (wanted is not null)
        {
            foreach (string missing in wanted.Except(names, StringComparer.Ordinal))
            {
                conflicts.Add(new("pages", "missing", missing, null));
            }
        }

        return conflicts.Count > 0 ? null : new(project with { Path = PlanSnapshotHashes.ProjectKey(project.Path) },
            SnapshotProfiles.StandardName, SnapshotProfiles.Format, [.. pages], wholeHash, PlanSnapshotHashes.Compose(pages),
            pages.ToImmutableDictionary(p => PlanSnapshotHashes.Key("page", p.Type, p.Oid), p => p.Hash, StringComparer.Ordinal),
            members.ToImmutableDictionary(StringComparer.Ordinal), PlanSnapshotHashes.Members(project.Path, members), skipped ?? 0);
    }

    private static void Compare(string scope, ImmutableDictionary<string, string> expected, ImmutableDictionary<string, string> actual, List<PlanSnapshotConflict> conflicts)
    {
        foreach (string key in expected.Keys.Union(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string? before = expected.GetValueOrDefault(key), after = actual.GetValueOrDefault(key);
            if (before != after)
            {
                conflicts.Add(new(scope + ":" + key, before is null ? "added" : after is null ? "removed" : "changed", before, after));
            }
        }
    }

    private static void Check<T>(string scope, T expected, T actual, List<PlanSnapshotConflict> conflicts)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            conflicts.Add(new(scope, "changed", System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual)));
        }
    }
}

using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Large.Tests;

/// <summary>Only reads LiveEplan's checked sample fixture; discovers every named function without device.get's limit.</summary>
internal sealed record LivePlanSample(LiveSnapshots.Snapshot Snapshot, List<DeviceListItem> Listed, List<PlanFunction> Functions)
{
    public PlanProject Project => new(Snapshot.Chunks[0].Project, Snapshot.Chunks[0].ProjectPath, Snapshot.Chunks[0].ProjectGuid);

    public static async Task<LivePlanSample> ReadAsync(BridgeClient bridge, string fixturePath)
    {
        LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new() { Project = fixturePath });
        Assert.True(snapshot.Chunks[^1].Complete, "An incomplete snapshot cannot justify a missing-sample skip.");
        Assert.Equal(snapshot.Chunks[0].PageCount, snapshot.Pages.Count);
        Assert.All(snapshot.Chunks, c =>
        {
            Assert.Equal(PlanSnapshotHashes.ProjectKey(fixturePath), PlanSnapshotHashes.ProjectKey(c.ProjectPath));
            Assert.Equal(SnapshotProfiles.Format, c.Format);
            Assert.Equal(SnapshotProfiles.StandardName, c.Profile);
            Assert.Equal(snapshot.Chunks[0].PageCount, c.PageCount);
            Assert.Empty(c.MissingPages);
        });
        var listed = new List<DeviceListItem>();
        int? total = null;
        do
        {
            ListData<DeviceListItem> part = await bridge.InvokeAsync<DeviceListArgs, ListData<DeviceListItem>>(OperationNames.DeviceList,
                new() { Project = fixturePath, IncludeAuxiliary = true, Offset = listed.Count, Limit = 500 }, TestContext.Current.CancellationToken);
            Assert.Equal(snapshot.Chunks[0].Project, part.Project);
            Assert.True(part.Total >= listed.Count, "The fixture's function listing shrank during discovery.");
            Assert.Equal(total ?? part.Total, part.Total);
            Assert.Equal(Math.Min(500, part.Total - listed.Count), part.Items.Count);
            listed.AddRange(part.Items);
            total = part.Total;
        } while (listed.Count < total);

        // OnePerTag merges mains, not names; the snapshot supplies additional placed representations (A12).
        string[] names = [.. listed.Select(f => f.Tag).Concat(snapshot.Objects("function").Select(f => f.Name).OfType<string>())
            .Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        var functions = new List<PlanFunction>();
        foreach (string[] chunk in names.Chunk(200))
        {
            PlanFunctionsData part = await bridge.InvokeAsync<PlanFunctionsArgs, PlanFunctionsData>(OperationNames.PlanFunctions,
                new() { Project = fixturePath, Names = [.. chunk] }, TestContext.Current.CancellationToken);
            Assert.True(part.Complete, "plan.functions returned complete=false; a truncated inventory cannot justify a skip or prove closure.");
            Assert.Equal((snapshot.Chunks[0].Project, 10_000), (part.Project, part.Limit));
            Assert.Equal(PlanSnapshotHashes.ProjectKey(fixturePath), PlanSnapshotHashes.ProjectKey(part.ProjectPath));
            Assert.InRange(part.Functions.Count, 0, part.Limit);
            Assert.All(part.Functions, f =>
            {
                Assert.Equal("function", f.Kind);
                Assert.True(f.Type > 0 && f.Oid > 0, $"Invalid persistent identity for {f.Name}: {f.Type}/{f.Oid}.");
                Assert.Contains(f.Name, chunk);
            });
            Assert.Equal(part.Functions.Select(Row), part.Functions.OrderBy(f => f.Name, StringComparer.Ordinal)
                .ThenBy(f => f.Page, StringComparer.Ordinal).ThenBy(f => f.Type).ThenBy(f => f.Oid).Select(Row));
            functions.AddRange(part.Functions);
        }

        Assert.Equal(functions.Count, functions.Select(f => (f.Type, f.Oid)).Distinct().Count());
        var sample = new LivePlanSample(snapshot, listed, functions);
        // Independent page evidence catches missing/extra mains and auxiliaries before any candidate-based skip.
        Assert.Equal(snapshot.Objects("function").Where(f => !string.IsNullOrEmpty(f.Name)).Select(f => (f.Type, f.Oid, f.Name, f.Main, f.Page))
                .OrderBy(f => f.Type).ThenBy(f => f.Oid),
            functions.Where(f => f.Page is not null).Select(f => (f.Type, f.Oid, Name: (string?)f.Name, Main: (bool?)f.IsMain, f.Page))
                .OrderBy(f => f.Type).ThenBy(f => f.Oid));
        foreach (var group in listed.Where(f => !f.Main).GroupBy(f => (f.Tag, f.Page)))
        {
            int actual = functions.Count(f => !f.IsMain && f.Name == group.Key.Tag && f.Page == group.Key.Page);
            if (group.Key.Page is null)
            {
                Assert.Equal(group.Count(), actual);
            }
            else
            {
                // Extra placed representations come from the snapshot; the unfiltered finder need not return those.
                Assert.True(actual >= group.Count(), $"plan.functions omitted listed auxiliaries for {group.Key.Tag} on {group.Key.Page}.");
            }
        }
        Assert.All(listed.Where(f => f.Main && f.Page is null), f =>
            Assert.Contains(functions, actual => actual.Name == f.Tag && actual.IsMain && actual.Page is null));
        TestContext.Current.TestOutputHelper?.WriteLine($"Fixture {fixturePath}: {names.Length} full names, {functions.Count} named functions, {functions.Count(f => f.Page is null)} page-less.");
        return sample;
    }

    public async Task<PlanBaseline> BaselineAsync(BridgeClient bridge, string[]? pages = null)
    {
        PlanBaselineRead read = await new PlanBaselineReader(bridge).ReadAsync(Project, pages, TestContext.Current.CancellationToken);
        Assert.Null(read.Reason);
        Assert.Empty(read.Conflicts);
        PlanBaseline baseline = Assert.IsType<PlanBaseline>(read.Baseline);
        Assert.All(baseline.Snapshot.Pages, p => Assert.Equal(Snapshot.Page(p.Name).Hash, p.Hash));
        return baseline;
    }

    public PlanFunction[] Related(string page)
    {
        var names = Snapshot.Page(page).Objects.Where(f => f.Kind == "function" && !string.IsNullOrEmpty(f.Name))
            .Select(f => f.Name!).ToHashSet(StringComparer.Ordinal);
        return [.. Functions.Where(f => names.Contains(f.Name))];
    }

    public static Dictionary<string, PlanFunction[]> Orphans(IEnumerable<PlanFunction> related, string deletedPage) => related
        .Where(f => f.IsMain && f.Page == deletedPage).Select(f => f.Name).Distinct(StringComparer.Ordinal)
        .Select(name => (Name: name, Left: related.Where(f => f.Name == name && f.Page != deletedPage).ToArray()))
        .Where(g => g.Left.Length > 0 && !g.Left.Any(f => f.IsMain)).ToDictionary(g => g.Name, g => g.Left, StringComparer.Ordinal);

    public static void Describe(string scenario, IEnumerable<PlanFunction> functions)
    {
        foreach (PlanFunction f in functions)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{scenario}: {f.Name}, {f.Type}/{f.Oid}, main={f.IsMain}, page={f.Page ?? "(page-less)"}");
        }
    }

    private static (string Name, string? Page, int Type, long Oid, bool Main) Row(PlanFunction f) => (f.Name, f.Page, f.Type, f.Oid, f.IsMain);
}

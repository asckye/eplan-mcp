using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;

namespace EplanMcp.Large.Tests;

/// <summary>A13/A18 impact closure only: no rename/delete plan or execute operation, and no fixture writes.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanImpactTests(LiveEplan eplan)
{
    [Fact]
    public async Task P06_A13_CrossPageRenameClosureIncludesAllFunctions()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var device = sample.Functions.GroupBy(f => f.Name, StringComparer.Ordinal)
            .OrderByDescending(g => g.Key == "=F+A2-KF1").ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Any(f => f.IsMain) && g.Any(f => !f.IsMain) && g.All(f => f.Page is not null)
                && g.Select(f => f.Page).Distinct(StringComparer.Ordinal).Count() is > 1 and <= 50);
        Assert.SkipWhen(device is null,
            "EPLAN_Sample_Project has no fully placed same-name device with main and auxiliary functions across 2–50 pages; all full names were checked.");
        PlanBaseline baseline = await sample.BaselineAsync(bridge);
        string mainPage = device!.First(f => f.IsMain).Page!;
        PlanImpactRead result = await new PlanImpactReader(bridge).ReadAsync(baseline,
            new("device.update", PlanScopeRules.Pages([mainPage]), device.Key), TestContext.Current.CancellationToken);
        Assert.Null(result.Reason);
        PlanImpact impact = Assert.IsType<PlanImpact>(result.Impact);
        Assert.Equal(device.Select(f => f.Page!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), impact.ModifyScope.Pages);
        Assert.Equal(device.Select(f => (f.Type, f.Oid)).OrderBy(f => f.Type).ThenBy(f => f.Oid),
            impact.ModifyScope.Locators.Select(f => (f.Type, f.Oid)).OrderBy(f => f.Type).ThenBy(f => f.Oid));
        foreach (PlanFunction function in device)
        {
            SnapshotObject obj = Assert.Single(baseline.Snapshot.Pages.SelectMany(p => p.Objects), o => o.Kind == "function" && o.Type == function.Type && o.Oid == function.Oid);
            PlanLocator locator = PlanScopeRules.Locator(baseline.Snapshot.Project.Path, obj);
            Assert.True(PlanScopeRules.Contains(impact.ReadScope, locator));
            Assert.True(PlanScopeRules.Contains(impact.ModifyScope, locator));
            Assert.Contains(impact.Preconditions.OfType<PlanObjectHash>(), p => p.Locator == locator && p.Hash == obj.Hash);
        }
        LivePlanSample.Describe("A13 rename closure (read only)", device);
    }

    [Fact]
    public async Task P06_A18_PageDeleteClosureIncludesRemainingFunctions()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var candidate = sample.Snapshot.Pages.OrderByDescending(p => p.Objects.Any(f => f.Name == "+A1-WE1" && f.Main == true))
            .ThenBy(p => p.Name, StringComparer.Ordinal).Select(p => (Page: p, Related: sample.Related(p.Name)))
            .FirstOrDefault(p => p.Related.All(f => f.Page is not null)
                && p.Related.Select(f => f.Page).Append(p.Page.Name).Distinct(StringComparer.Ordinal).Count() <= 50
                && LivePlanSample.Orphans(p.Related, p.Page.Name).Count > 0);
        Assert.SkipWhen(candidate.Page is null,
            "EPLAN_Sample_Project has no page that would orphan a same-name device while every related function is placed within 50 pages; all sample pages were checked.");
        string page = candidate.Page!.Name;
        Dictionary<string, PlanFunction[]> expected = LivePlanSample.Orphans(candidate.Related, page);
        PlanBaseline baseline = await sample.BaselineAsync(bridge);
        PlanImpactRead result = await new PlanImpactReader(bridge).ReadAsync(baseline,
            new("page.delete", PlanScopeRules.Pages([page])), TestContext.Current.CancellationToken);
        Assert.Null(result.Reason);
        PlanImpact impact = Assert.IsType<PlanImpact>(result.Impact);
        AssertOrphans(expected, result.Orphaned);
        Assert.Equal(candidate.Related.Select(f => f.Page!).Append(page).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), impact.ReadScope.Pages);
        var expectedModified = candidate.Page.Objects.Select(f => (f.Kind, f.Type, f.Oid))
            .Concat(expected.Values.SelectMany(f => f).Select(f => (f.Kind, f.Type, f.Oid))).ToHashSet();
        Assert.Equal(expectedModified.OrderBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.Type).ThenBy(f => f.Oid),
            PlanScopeRules.Select(baseline.Snapshot, impact.ModifyScope).Select(f => (f.Kind, f.Type, f.Oid)));
        LivePlanSample.Describe($"A18 delete closure for {page} (read only)", expected.Values.SelectMany(f => f));

        // Real evidence deliberately limited to the deleted page cannot prove the surviving functions on other pages.
        PlanBaseline partial = await sample.BaselineAsync(bridge, [page]);
        PlanImpactRead uncovered = await new PlanImpactReader(bridge).ReadAsync(partial,
            new("page.delete", PlanScopeRules.Pages([page])), TestContext.Current.CancellationToken);
        Assert.Null(uncovered.Impact);
        Assert.Equal(ErrorReason.CapabilityMissing, uncovered.Reason);
        Assert.Contains("snapshot:functions", uncovered.Missing);
    }

    [Fact]
    public async Task P06_A18_PageDeleteWithUnplacedFunctionsIsCapabilityMissing()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LivePlanSample sample = await LivePlanSample.ReadAsync(bridge, eplan.ProjectPath);
        var candidate = sample.Snapshot.Pages.OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => (Page: p, Related: sample.Related(p.Name))).FirstOrDefault(p => p.Related.Any(f => f.Page is null));
        Assert.SkipWhen(candidate.Page is null,
            "EPLAN_Sample_Project has no placed function sharing its full name with a page-less function; all full names and sample pages were checked.");
        string page = candidate.Page!.Name;
        PlanBaseline baseline = await sample.BaselineAsync(bridge);
        PlanImpactRead result = await new PlanImpactReader(bridge).ReadAsync(baseline,
            new("page.delete", PlanScopeRules.Pages([page])), TestContext.Current.CancellationToken);
        Assert.Null(result.Impact);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Reason);
        Assert.Contains("snapshot:functions_without_page", result.Missing);
        AssertOrphans(LivePlanSample.Orphans(candidate.Related, page), result.Orphaned);
        LivePlanSample.Describe($"A18 missing page evidence for {page} (read only)", candidate.Related.Where(f => f.Page is null));
    }

    private static void AssertOrphans(Dictionary<string, PlanFunction[]> expected, IEnumerable<PlanOrphan> actual)
    {
        PlanOrphan[] orphans = [.. actual];
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), orphans.Select(o => o.Name).Order(StringComparer.Ordinal));
        foreach (PlanOrphan orphan in orphans)
        {
            Assert.Equal(expected[orphan.Name].Select(f => (f.Type, f.Oid, f.Name, f.Page)).OrderBy(f => f.Type).ThenBy(f => f.Oid),
                orphan.Remaining.Select(f => (f.Type, f.Oid, Name: f.Name!, f.Page)).OrderBy(f => f.Type).ThenBy(f => f.Oid));
        }
    }
}

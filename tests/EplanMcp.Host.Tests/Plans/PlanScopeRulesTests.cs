using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanScopeRulesTests
{
    [Theory]
    [InlineData("+A1/1", "+A1", true)]
    [InlineData("+A10/1", "+A1", false)]
    [InlineData("+A1&EFS/1", "+A1", true)]
    [InlineData("+A1&EFS10/1", "+A1&EFS1", false)]
    [InlineData("=F+A1&EFS/1", "=F+A1", true)]
    [InlineData("=F1+A1/1", "=F", false)]
    [InlineData("+a1/1", "+A1", false)]
    [InlineData("++A1/1", "+A1", false)]
    public void Structures_ArePrefixesOfCompleteSegments(string page, string prefix, bool expected) =>
        Assert.Equal(expected, PlanScopeRules.StructurePrefix(page, prefix));

    [Fact]
    public void Filters_Intersect_AndNeverWidenAnEmptyResult()
    {
        PlanSnapshot snapshot = PlanScopeTestData.Baseline().Snapshot;
        SnapshotObject function = snapshot.Pages[0].Objects[1];
        PlanLocator locator = PlanScopeRules.Locator(snapshot.Project.Path, function);
        var scope = new PlanScope(["+A1/1", "+A10/1"], ["+A1"], [locator]);
        Assert.Equal(function, Assert.Single(PlanScopeRules.Select(snapshot, scope)));
        Assert.Empty(PlanScopeRules.Select(snapshot, scope with { Pages = ["+A10/1"] }));
        Assert.Empty(PlanScopeRules.Select(snapshot, new([], [], [])));
        Assert.Equal(6, PlanScopeRules.Select(snapshot, PlanScopeRules.Whole(snapshot)).Length);
    }

    [Fact]
    public void Locators_BindCopiesAndEveryDescription_WithObjectResolvePositionTolerance()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanLocator locator = PlanScopeRules.Locator(baseline.Snapshot.Project.Path, baseline.Snapshot.Pages[0].Objects[1]);
        Assert.True(PlanScopeRules.Same(locator with { Project = @"d:\projects\.\Line1.elk", Pos = "10.001,19.999" }, locator));
        foreach (PlanLocator changed in new[] { locator with { Project = @"D:\Copy\Line1" }, locator with { Kind = "page" }, locator with { Type = 18 },
            locator with { Oid = 99 }, locator with { Name = "other" }, locator with { Page = "+A1/2" }, locator with { Category = "Motor" }, locator with { Pos = "10.002,20" } })
        {
            Assert.False(PlanScopeRules.Same(changed, locator));
            Assert.Equal(["snapshot:locators"], PlanScopeRules.Missing(baseline, new([], [], [changed])));
        }
        Assert.True(PlanScopeRules.Same(locator with { Name = null, Page = null, Category = null, Pos = null }, locator));
    }

    [Fact]
    public void StructureScope_PartialSnapshotAndWildcard_AreMissingCapabilities()
    {
        PlanBaseline whole = PlanScopeTestData.Baseline();
        PlanBaseline partial = whole with { Snapshot = PlanScopeTestData.Baseline(pages: ["+A1/1"]).Snapshot };
        Assert.Equal(["snapshot:pages"], PlanScopeRules.Missing(partial, new([], ["+A1"], [])));
        Assert.Equal(["scope:structure_segments"], PlanScopeRules.Missing(whole, new([], ["+A*"], [])));
    }

    [Fact]
    public void Resolve_CanonicalizesSets_AndFillsTheCompleteSnapshotDescription()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        var locator = new PlanLocator(@"d:\projects\Line1.elk", "function", 17, 11, null, null, null, null);
        var scope = new PlanScope(["+A1/2", "+A1/1", "+A1/1"], ["+A1", "+A1"], [locator, locator]);
        PlanScope resolved = PlanScopeRules.Resolve(baseline.Snapshot, scope);
        Assert.Equal(["+A1/1", "+A1/2"], resolved.Pages);
        Assert.Equal(["+A1"], resolved.Structures);
        Assert.Equal(PlanScopeRules.Locator(baseline.Snapshot.Project.Path, baseline.Snapshot.Pages[0].Objects[1]), Assert.Single(resolved.Locators));
    }
}

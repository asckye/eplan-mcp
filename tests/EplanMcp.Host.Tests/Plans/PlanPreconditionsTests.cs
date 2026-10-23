using System.Text.Json;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanPreconditionsTests
{
    [Fact]
    public void Build_ProducesAllSixTags_WithSnapshotLocatorsAndContentHashes()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanScope scope = PlanScopeRules.Pages(["+A1/1", "+A1/1"]);
        PlanPrecondition[] conditions = [.. PlanPreconditions.Build(baseline, scope,
            [new("page", "+A1/3"), new("page", "+A1/3")], [new("p", "1.0.0", "m.ema", "manifest", "file")], new(3, "locks"))];
        Assert.Equal(2, conditions.OfType<PlanObjectHash>().Count());
        Assert.Equal(baseline.Snapshot.Pages[0].Hash, Assert.Single(conditions.OfType<PlanPageHash>()).Hash);
        Assert.Equal(2, Assert.Single(conditions.OfType<PlanMembers>()).Count);
        Assert.Single(conditions.OfType<PlanAbsent>());
        Assert.Equal("file", Assert.Single(conditions.OfType<PlanResourceHash>()).Sha256);
        Assert.Equal(3, Assert.Single(conditions.OfType<PlanBinding>()).Revision);
        string json = JsonSerializer.Serialize(conditions, PlanJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(PlanJson.Read<PlanPrecondition[]>(json), PlanJson.Options));
        Assert.All(conditions.OfType<PlanObjectHash>(), c => Assert.Equal(baseline.Snapshot.Project.Path, c.Locator.Project));
    }

    [Fact]
    public void Members_IncludeAdditionsDeletionsAndChanges_ButNotUnrelatedPages()
    {
        PlanScope scope = PlanScopeRules.Pages(["+A1/1"]);
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        string Hash(PlanBaseline value) => Assert.Single(PlanPreconditions.Build(value, scope).OfType<PlanMembers>()).Hash;
        string before = Hash(baseline);
        Assert.Equal(before, Hash(PlanScopeTestData.Baseline([PlanScopeTestData.Function(11, "+A1/1", main: true), PlanScopeTestData.Function(99, "+A10/1")])));
        Assert.NotEqual(before, Hash(PlanScopeTestData.Baseline([])));
        Assert.NotEqual(before, Hash(PlanScopeTestData.Baseline([PlanScopeTestData.Function(11, "+A1/1", main: true), PlanScopeTestData.Function(99, "+A1/1")])));
        var changed = PlanScopeTestData.Function(11, "+A1/1", main: true);
        changed.Hash = "changed";
        Assert.NotEqual(before, Hash(PlanScopeTestData.Baseline([changed])));
        Assert.NotEqual(before, Hash(PlanScopeTestData.Baseline(project: @"D:\Copy\Line1")));
        Assert.NotEqual(before, Hash(PlanScopeTestData.Baseline(pages: ["+A1/1", "+A1/2"], functions: [PlanScopeTestData.Function(15, "+A1/1", main: true)])));
    }

    [Fact]
    public void Build_MissingCoverageOrOccupiedNames_DoesNotEmitFalsePreconditions()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        Assert.Throws<ArgumentException>(() => PlanPreconditions.Build(baseline, PlanScopeRules.Pages(["missing"])));
        Assert.Throws<ArgumentException>(() => PlanPreconditions.Build(baseline, PlanScopeRules.Whole(baseline.Snapshot), [new("page", "+a1/1")]));
        Assert.Throws<ArgumentException>(() => PlanPreconditions.Build(baseline, PlanScopeRules.Whole(baseline.Snapshot), [new("function", "+a1-K1")]));
    }
}

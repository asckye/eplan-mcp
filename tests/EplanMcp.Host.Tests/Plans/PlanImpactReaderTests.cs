using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Bridge;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanImpactReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeviceUpdate_ClosesAcrossPagesIncludingRepeatedMains_AndReadsCollisionDomain()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline([
            PlanScopeTestData.Function(11, "+A1/1", main: true), PlanScopeTestData.Function(12, "+A1/2", main: true),
            PlanScopeTestData.Function(13, "+A1/2"), PlanScopeTestData.Function(14, "+A10/1", "+A10-K2", true)]);
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(baseline));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline,
            new("device.update", PlanScopeRules.Pages(["+A1/1"]), "+A1-K1"), Token);
        PlanImpact impact = Assert.IsType<PlanImpact>(result.Impact);
        Assert.Null(result.Reason);
        Assert.Equal(["+A1/1", "+A1/2"], impact.ModifyScope.Pages);
        Assert.Equal([11L, 12, 13], impact.ModifyScope.Locators.Select(l => l.Oid));
        Assert.Contains("+A10/1", impact.ReadScope.Pages);
        Assert.Equal(baseline.NamesHash, impact.NamesHash);
        Assert.Equal(7, Assert.Single(impact.Preconditions.OfType<PlanMembers>()).Count);
    }

    [Fact]
    public async Task DeviceUpdate_MoreThanFiftyAuxiliaries_IsNeverReducedToDetails()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline(Enumerable.Range(11, 61)
            .Select(i => PlanScopeTestData.Function(i, i == 11 ? "+A1/1" : "+A1/2", main: i == 11)));
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(baseline));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("device.update", new([], [], []), "+A1-K1"), Token);
        Assert.Equal(61, Assert.IsType<PlanImpact>(result.Impact).ModifyScope.Locators.Length);
    }

    [Fact]
    public async Task DeviceUpdate_UnplacedFunction_IsCapabilityMissingWithoutAnImpact()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new PlanFunctionsBridge([.. PlanScopeTestData.Functions(baseline), new() { Type = 17, Oid = 99, Name = "+A1-K1" }]);
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("device.update", new([], [], []), "+A1-K1"), Token);
        Assert.Null(result.Impact);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Reason);
        Assert.Contains("snapshot:functions_without_page", result.Missing);
    }

    [Fact]
    public async Task DeviceUpdate_PartialCoverage_CannotFallBackToTheMainPage()
    {
        PlanBaseline whole = PlanScopeTestData.Baseline();
        PlanBaseline partial = whole with { Snapshot = PlanScopeTestData.Baseline(pages: ["+A1/1"]).Snapshot };
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(whole));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(partial, new("device.update", new([], [], []), "+A1-K1"), Token);
        Assert.Null(result.Impact);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Reason);
        Assert.Contains("snapshot:functions", result.Missing);
        Assert.Contains("snapshot:project_pages", result.Missing);
    }

    [Fact]
    public async Task PageDelete_OrphansAreComplete_AndRemainingFunctionsJoinModifyScope()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(baseline));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("page.delete", PlanScopeRules.Pages(["+A1/1"])), Token);
        PlanImpact impact = Assert.IsType<PlanImpact>(result.Impact);
        Assert.Equal("+A1-K1", Assert.Single(result.Orphaned).Name);
        Assert.Equal(12, Assert.Single(result.Orphaned[0].Remaining).Oid);
        Assert.Equal(["+A1/1", "+A1/2"], impact.ReadScope.Pages);
        Assert.Equal(["+A1/1", "+A1/2"], impact.ModifyScope.Pages);
        Assert.Contains(impact.ModifyScope.Locators, l => l.Kind == "page" && l.Name == "+A1/1");
        Assert.Contains(impact.ModifyScope.Locators, l => l.Kind == "function" && l.Oid == 12);
        Assert.DoesNotContain(impact.ModifyScope.Locators, l => l.Kind == "page" && l.Name == "+A1/2");
    }

    [Fact]
    public async Task PageDelete_AnotherMainStays_DeviceIsNotOrphaned()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline([
            PlanScopeTestData.Function(11, "+A1/1", main: true), PlanScopeTestData.Function(12, "+A1/2", main: true), PlanScopeTestData.Function(13, "+A1/2")]);
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(baseline));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("page.delete", PlanScopeRules.Pages(["+A1/1"])), Token);
        Assert.NotNull(result.Impact);
        Assert.Empty(result.Orphaned);
        Assert.Contains("+A1/2", result.Impact.ReadScope.Pages);
        Assert.Equal(["+A1/1"], result.Impact.ModifyScope.Pages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageDelete_UnplacedRemainingFunction_IsObservedThenRefused(bool main)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new PlanFunctionsBridge([.. PlanScopeTestData.Functions(baseline), new() { Type = 17, Oid = 99, Name = "+A1-K1", IsMain = main }]);
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("page.delete", PlanScopeRules.Pages(["+A1/1"])), Token);
        Assert.Null(result.Impact);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Reason);
        Assert.Contains("snapshot:functions_without_page", result.Missing);
        if (main)
        { Assert.Empty(result.Orphaned); }
        else
        { Assert.Contains(Assert.Single(result.Orphaned).Remaining, l => l.Oid == 99 && l.Page is null); }
    }

    [Fact]
    public async Task PageDelete_MoreThanTwoHundredDiscoveredNames_UsesBoundedBatches()
    {
        SnapshotObject[] functions = [.. Enumerable.Range(11, 201).Select(i => PlanScopeTestData.Function(i, "+A1/1", "+A1-K" + i, true))];
        PlanBaseline baseline = PlanScopeTestData.Baseline(functions);
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(baseline));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("page.delete", PlanScopeRules.Pages(["+A1/1"])), Token);
        Assert.NotNull(result.Impact);
        Assert.Equal(202, result.Impact.ModifyScope.Locators.Length);
        Assert.Equal([200, 1], fake.Calls.Select(a => a.Names.Count));
    }

    [Fact]
    public async Task PageDelete_MoreThanFiftyOrphans_AreNotTruncatedLikePlanDetails()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline(Enumerable.Range(11, 61).SelectMany(i => new[]
        {
            PlanScopeTestData.Function(i, "+A1/1", "+A1-K" + i, true), PlanScopeTestData.Function(i + 1000, "+A1/2", "+A1-K" + i),
        }));
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(baseline));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("page.delete", PlanScopeRules.Pages(["+A1/1"])), Token);
        Assert.NotNull(result.Impact);
        Assert.Equal(61, result.Orphaned.Length);
        Assert.All(result.Orphaned, orphan => Assert.Equal("+A1/2", Assert.Single(orphan.Remaining).Page));
    }

    [Fact]
    public async Task DeviceUpdate_ClosureOverFiftyPages_IsInvalidWithoutDroppingPages()
    {
        string[] pages = [.. Enumerable.Range(1, 51).Select(i => "+A1/" + i)];
        PlanBaseline baseline = PlanScopeTestData.Baseline(pages.Select((p, i) => PlanScopeTestData.Function(100 + i, p, main: i == 0)), pages);
        await using var fake = new PlanFunctionsBridge(PlanScopeTestData.Functions(baseline));
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("device.update", new([], [], []), "+A1-K1"), Token);
        Assert.Null(result.Impact);
        Assert.Equal(ErrorReason.InvalidArgument, result.Reason);
        Assert.Equal(["/modifyScope/pages"], result.Violations);
    }

    [Fact]
    public async Task DeviceUpdate_ReaderDisagreesOnMainFlag_IsMissingEvidence()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanFunction[] functions = PlanScopeTestData.Functions(baseline);
        functions[0].IsMain = false;
        await using var fake = new PlanFunctionsBridge(functions);
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("device.update", new([], [], []), "+A1-K1"), Token);
        Assert.Null(result.Impact);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Reason);
        Assert.Contains("snapshot:functions", result.Missing);
    }

    [Fact]
    public async Task DeviceUpdate_IncompleteReader_IsCapabilityMissing()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new PlanFunctionsBridge([.. Enumerable.Range(1, 10_001).Select(i => new PlanFunction { Type = 17, Oid = i, Name = "+A1-K1" })]);
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("device.update", new([], [], []), "+A1-K1"), Token);
        Assert.Null(result.Impact);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Reason);
        Assert.Contains("plan.functions:complete", result.Missing);
    }

    [Theory]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public async Task Renumber_ModifyScopeIsTheWholeProject_AndStillBounded(string op)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new PlanFunctionsBridge([]);
        PlanImpactReader reader = new(fake.Client);
        PlanImpactRead small = await reader.ReadAsync(baseline, new(op, PlanScopeRules.Pages(["+A1/1"])), Token);
        Assert.Equal(["+A1/1", "+A1/2", "+A10/1"], Assert.IsType<PlanImpact>(small.Impact).ModifyScope.Pages);
        PlanBaseline large = PlanScopeTestData.Baseline([], [.. Enumerable.Range(1, 51).Select(i => "+A1/" + i)]);
        PlanImpactRead refused = await reader.ReadAsync(large, new(op, PlanScopeRules.Pages(["+A1/1"])), Token);
        Assert.Null(refused.Impact);
        Assert.Equal(ErrorReason.InvalidArgument, refused.Reason);
        Assert.Contains("/modifyScope/pages", refused.Violations);
        Assert.Equal(ErrorReason.InvalidArgument, (await reader.ReadAsync(baseline, new(op, new([], [], [])), Token)).Reason);
    }

    [Theory]
    [InlineData("page.create")]
    [InlineData("macro.insert")]
    [InlineData("page.update")]
    public async Task PageOperations_UseCollisionReadsOrPageReads_AndPreciseModifyScopes(string op)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new PlanFunctionsBridge([]);
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new(op, PlanScopeRules.Pages([op == "page.create" ? "+A1/3" : "+A1/1"])), Token);
        PlanImpact impact = Assert.IsType<PlanImpact>(result.Impact);
        Assert.Single(impact.ModifyScope.Pages);
        if (op == "page.update")
        {
            Assert.Equal("page", Assert.Single(impact.ModifyScope.Locators).Kind);
            Assert.Single(impact.ReadScope.Pages);
        }
        else
        { Assert.Equal(3, impact.ReadScope.Pages.Length); }
        if (op == "page.create")
        { Assert.Equal("+A1/3", Assert.Single(impact.Preconditions.OfType<PlanAbsent>()).Name); }
    }

    [Theory]
    [InlineData(9997, true)]
    [InlineData(9998, false)]
    public async Task Guard_TenThousandObjectBoundary_IsEnforcedWithoutShrinking(int functions, bool accepted)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline(Enumerable.Range(11, functions).Select(i => PlanScopeTestData.Function(i, "+A1/1")));
        await using var fake = new PlanFunctionsBridge([]);
        PlanImpactRead result = await new PlanImpactReader(fake.Client).ReadAsync(baseline, new("macro.insert", PlanScopeRules.Pages(["+A1/2"])), Token);
        if (accepted)
        { Assert.NotNull(result.Impact); Assert.Null(result.Reason); }
        else
        {
            Assert.Null(result.Impact);
            Assert.Equal(ErrorReason.InvalidArgument, result.Reason);
            Assert.Contains("/readScope/objects", result.Violations);
        }
    }
}

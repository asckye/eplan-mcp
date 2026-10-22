using EplanMcp.Coverage.Reporting;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Medium")]
public sealed class CoverageReportTests : IDisposable
{
    private const string Page = ReportFixture.Page;
    private readonly TempRepository _repository = ReportFixture.Create();

    [Fact]
    public void Build_TheOperationLayer_CountsOverloadGroups_WithExclusionsStatedAndStatusesNested()
    {
        LayerCount operation = CoverageReport.Build(_repository.Root).Layers["operation"];

        // Create (two overloads, one used), Name, Remove (undocumented), Old (deprecated), Planning.Plan.Run (env_blocked).
        Assert.Equal((5, 4), (operation.Registered, operation.Denominator));
        Assert.Equal(1, Assert.Single(operation.Excluded, e => e.Key == "deprecated").Value);
        Assert.Equal((3, 2, 1), (operation.Implemented, operation.ByTools, operation.Partial));
        Assert.Equal((1, 1, 1, 1), (operation.AutomatedTest, operation.LiveVerified, operation.EnvBlocked, operation.Undocumented));
    }

    [Fact]
    public void Build_MethodsAndProperties_AreAlsoCountedApart()
    {
        CoverageReport report = CoverageReport.Build(_repository.Root);

        Assert.Equal((4, 3, 2), (report.Layers["operation.method"].Registered, report.Layers["operation.method"].Denominator, report.Layers["operation.method"].Implemented));
        Assert.Equal((1, 1, 1), (report.Layers["operation.property"].Registered, report.Layers["operation.property"].Denominator, report.Layers["operation.property"].Implemented));
        Assert.Equal([("Eplan.EplApi.Test", 1)], report.UndocumentedByNamespace.Select(s => (s.Scope, s.Denominator)));
    }

    [Fact]
    public void Build_EachUnit_GetsTheStatusOfTheBestEvidenceOfTheToolsThatReachIt()
    {
        Dictionary<string, UnitCoverage> units = CoverageReport.Build(_repository.Root).Units.ToDictionary(u => u.Unit);

        // eplan_page_list has a Small test and a stated live record; eplan_page_create only a Large test class.
        Assert.Equal(("live_verified", "eplan_page_list"), (units[$"O:{Page}.Name"].Status, Assert.Single(units[$"O:{Page}.Name"].Tools)));
        Assert.Equal(("implemented", true), (units[$"O:{Page}.Create"].Status, units[$"O:{Page}.Create"].Partial));
        Assert.Equal(("implemented", 0), (units[$"O:{Page}.Remove"].Status, units[$"O:{Page}.Remove"].Tools.Count)); // infrastructure only
        Assert.Equal("live_verified", units["D:Page"].Status);
        Assert.Equal(["A:export/PDF"], units.Keys.Where(k => k.StartsWith("A:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_TheDataDictionaryCountsObjectTypes_ActionsCountRows_AndDependenciesAreCountedApart()
    {
        CoverageReport report = CoverageReport.Build(_repository.Root);

        Assert.Equal((2, 1, 1, 1), Counts(report.Layers["data_dictionary"])); // Page, and Retired whose every property is obsolete
        Assert.Equal(new UsedCount(1, 1), report.DictionaryProperties);
        Assert.Equal((3, 2, 1, 0), Counts(report.Layers["action"]));
        Assert.Equal("printing", Assert.Single(report.Layers["action"].Excluded).Key);
        Assert.Equal((2, 2, 1, 0), Counts(report.Layers["action.type"]));
        Assert.Equal(new UsedCount(1, 1), report.ActionNames);
        Assert.Equal(new UsedCount(2, 5), report.Dependencies);
        Assert.Equal([$"M:{Page}.Missing"], report.Unmatched);
    }

    [Fact]
    public void Build_TheToolView_CountsEachToolsUnitsAndEvidence()
    {
        CoverageReport report = CoverageReport.Build(_repository.Root);
        ToolCoverage list = report.Tools.Single(t => t.Name == "eplan_page_list");
        ToolCoverage create = report.Tools.Single(t => t.Name == "eplan_page_create");

        Assert.Equal((4, 1, 1, 1, 0), (list.ApiIds, list.OperationGroups, list.DataDictionary, list.Dependencies, list.Actions));
        Assert.Equal(("2026-10-19", 1, 0), (list.LatestLive!.Date, list.Small, list.Large));
        Assert.Equal((1, 1, 1), (create.OperationGroups, create.Actions, create.Large));
        Assert.Null(create.LatestLive);
        Assert.Equal(["Eplan.EplApi.Test", "Eplan.EplApi.Test.Planning"], report.OperationsByNamespace.Select(s => s.Scope));
    }

    [Fact]
    public void Build_WithoutACatalog_CountsEmptyLayersAndCallsNothingUnmatched()
    {
        using TempRepository bare = ReportFixture.Create(withCatalog: false);

        CoverageReport report = CoverageReport.Build(bare.Root);

        Assert.Equal((false, false), (report.SdkCatalog, report.ActionCatalog));
        Assert.All(report.Layers.Values, l => Assert.Equal(0, l.Registered));
        Assert.Empty(report.Unmatched);
        Assert.Equal(4, report.Tools.Single(t => t.Name == "eplan_page_list").ApiIds);
    }

    public void Dispose() => _repository.Dispose();

    private static (int Registered, int Denominator, int Implemented, int LiveVerified) Counts(LayerCount layer) =>
        (layer.Registered, layer.Denominator, layer.Implemented, layer.LiveVerified);
}

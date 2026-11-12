using EplanMcp.Coverage.Reporting;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Small")]
public sealed class CoverageRoutesTests
{
    [Theory]
    [InlineData("eplan_api_try", "api_try_only")]
    [InlineData("eplan_api_get", "api_try_only")]
    [InlineData("eplan_api_search", "api_try_only")]
    [InlineData("eplan_action_try", "api_try_only")]
    [InlineData("eplan_action_run", "api_try_only")]
    [InlineData("eplan_object_get", "generic_read")]
    [InlineData("eplan_object_find", "generic_read")]
    [InlineData("eplan_property_get", "generic_read")]
    [InlineData("eplan_property_schema", "generic_read")]
    [InlineData("eplan_property_set", "generic_read")]
    [InlineData("eplan_sample_task", "dedicated")]
    public void Classify_SingleToolUsesItsAssignedRoute(string tool, string expected)
    {
        Assert.Equal(expected, CoverageRoutes.Classify([tool], "implemented", true));
    }

    [Fact]
    public void Classify_DedicatedToolTakesPriorityOverBothGenericFamilies()
    {
        Assert.Equal("dedicated", CoverageRoutes.Classify(["eplan_sample_task", "eplan_api_try"], "live_verified", true));
        Assert.Equal("dedicated", CoverageRoutes.Classify(["eplan_property_set", "eplan_sample_task", "eplan_api_try"], "automated_test", true));
    }

    [Fact]
    public void Classify_GenericReadTakesPriorityOverGenericEntry()
    {
        Assert.Equal("generic_read", CoverageRoutes.Classify(["eplan_api_try", "eplan_property_set"], "implemented", true));
    }

    [Theory]
    [InlineData("implemented")]
    [InlineData("automated_test")]
    [InlineData("live_verified")]
    public void Classify_ExistingStatusWithoutToolsIsStateOnly(string status)
    {
        Assert.Equal("state_only", CoverageRoutes.Classify([], status, true));
    }

    [Fact]
    public void Classify_CallableWithoutToolsOrImplementationStatusIsOther()
    {
        Assert.Equal("other", CoverageRoutes.Classify([], "registered", true));
    }

    [Theory]
    [InlineData("registered")]
    [InlineData("env_blocked")]
    public void Classify_UnreachableWithoutToolsIsNone(string status)
    {
        Assert.Equal("none", CoverageRoutes.Classify([], status, false));
    }

    [Fact]
    public void Count_PartitionsTheDenominatorAndLiveEvidenceIntoAllSevenRoutes()
    {
        (string Route, bool LiveVerified)[] units =
        [
            ("dedicated", true), ("dedicated", false), ("generic_read", true),
            ("api_try_only", true), ("state_only", false), ("other", false), ("declined", false), ("none", false),
        ];

        SortedDictionary<string, RouteCount> counts = CoverageRoutes.Count(units);

        Assert.Equal(7, counts.Count);
        Assert.Equal(units.Length, counts.Values.Sum(c => c.Count));
        Assert.Equal(3, counts.Values.Sum(c => c.LiveVerified));
        Assert.Equal(new RouteCount(2, 1), counts["dedicated"]);
        Assert.Equal(new RouteCount(1, 1), counts["generic_read"]);
        Assert.Equal(new RouteCount(1, 1), counts["api_try_only"]);
        Assert.Equal(new RouteCount(1, 0), counts["state_only"]);
        Assert.Equal(new RouteCount(1, 0), counts["other"]);
        Assert.Equal(new RouteCount(1, 0), counts["declined"]);
        Assert.Equal(new RouteCount(1, 0), counts["none"]);
    }

    [Fact]
    public void Count_EmptyDenominatorKeepsAllRoutesWithZeroCounts()
    {
        SortedDictionary<string, RouteCount> counts = CoverageRoutes.Count([]);

        Assert.Equal(7, counts.Count);
        Assert.All(counts.Values, count => Assert.Equal(new RouteCount(0, 0), count));
    }

    [Fact]
    public void Declined_TakesGroupsWhoseEverySignatureIsExcludedForAClosingReason()
    {
        string report = string.Join("\n",
            "group\tsignature\tstatus\treason",
            "O:A.Settings\tM:A.Settings.Set(System.String)\texcluded\tsettings_write",
            "O:A.Settings\tM:A.Settings.Set(System.Int32)\texcluded\tsettings_write",
            "O:A.Mixed\tM:A.Mixed.Get\tread\tproperty_read",
            "O:A.Mixed\tM:A.Mixed.Set\texcluded\tsettings_write",
            "O:A.Copy\tM:A.Copy.Run\texcluded\tmasterdata_copy_pending",
            "O:A.Path\tM:A.Path.Open(System.String)\texcluded\texternal_path_parameter\r");

        Dictionary<string, string> declined = CoverageRoutes.Declined(report);

        Assert.Equal(["O:A.Path", "O:A.Settings"], declined.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("settings_write", declined["O:A.Settings"]);
        Assert.Equal("external_path_parameter", declined["O:A.Path"]);
    }

    [Fact]
    public void Headline_ShowsEachRouteAndTheExistingLiveEvidence()
    {
        SortedDictionary<string, RouteCount> counts = CoverageRoutes.Count(
            [("dedicated", true), ("dedicated", false), ("state_only", false), ("none", false)]);

        Assert.Equal("操作层 4 组：专用工具 2，通用读取 0，仅通用入口 0，仅状态覆盖 1，其他可调用 0，明确不做 0，到不了 1；实机验证 1",
            CoverageRoutes.Headline(4, counts));
    }
}

[Trait("Size", "Medium")]
public sealed class CoverageRouteReportTests
{
    [Fact]
    public void Build_RouteTotalsMatchTheOperationDenominatorAndExistingEvidence()
    {
        using TempRepository repository = ReportFixture.Create();

        CoverageReport report = CoverageReport.Build(repository.Root);
        SortedDictionary<string, RouteCount> routes = Assert.IsType<SortedDictionary<string, RouteCount>>(report.Routes);

        Assert.Equal(report.Layers["operation"].Denominator, routes.Values.Sum(c => c.Count));
        Assert.Equal(report.Reachability!.Operation.Callable, routes.Values.Sum(c => c.Count) - routes["none"].Count - routes["declined"].Count);
        Assert.Equal(report.Layers["operation"].LiveVerified, routes.Values.Sum(c => c.LiveVerified));
        Assert.Equal(new RouteCount(2, 1), routes["dedicated"]);
        Assert.Equal(new RouteCount(1, 0), routes["state_only"]);
        Assert.Equal(new RouteCount(1, 0), routes["none"]);
        Assert.All(report.Units.Where(u => u.Layer == "operation"), unit => Assert.NotNull(unit.Route));
        Assert.All(report.Units.Where(u => u.Layer != "operation"), unit => Assert.Null(unit.Route));
    }
}

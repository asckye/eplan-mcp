using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class PageReviewRulesTests
{
    private void AssertNetOverlap(string? severity, double[]? position = null)
    {
        JArray issues = Review("net_overlap");
        if (severity is null)
        {
            Assert.Empty(issues);
            return;
        }
        JToken issue = Assert.Single(issues);
        Assert.Equal(("net_overlap", severity, Page), ((string?)issue["code"], (string?)issue["severity"], (string?)issue["page"]));
        Assert.StartsWith(severity == "error" ? "R5:" : "R6:", (string?)issue["message"], StringComparison.Ordinal);
        Assert.Empty(Assert.IsType<JArray>(issue["objects"]));
        Assert.Equal(position ?? [20, 0], issue["position"]!.ToObject<double[]>());
        Assert.Equal(Page, (string?)issue["fix"]!["args"]!["page"]);
    }

    private void ParallelWires(double separation)
    {
        _scene.Graphical.Add([[0, 0], [40, 0]]);
        _scene.Graphical.Add([[20, separation], [60, separation]]);
    }

    [Theory]
    [InlineData(-double.Epsilon, "warning")]
    [InlineData(0, "error")]
    [InlineData(double.Epsilon, "warning")]
    public void NetOverlap_ZeroSeparation_ReportsR5OnlyAtCoincidence(double y, string severity)
    {
        ParallelWires(y);
        AssertNetOverlap(severity);
    }

    [Theory]
    [InlineData(3.9999999999999996, "warning")]
    [InlineData(4, null)]
    [InlineData(4.000000000000001, null)]
    public void NetOverlap_DefaultSpacing_UsesPageGridAsExclusiveLimit(double y, string? severity)
    {
        ParallelWires(y);
        AssertNetOverlap(severity);
    }

    [Theory]
    [InlineData(1.9999999999999998, "warning")]
    [InlineData(2, null)]
    [InlineData(2.0000000000000004, null)]
    public void NetOverlap_ConfiguredSpacing_OverridesGridEvenInStrictReview(double x, string? severity)
    {
        _scene.Layout.Strict = true;
        _scene.Layout.ParallelSpacingMm = 2;
        _scene.Graphical.Add([[0, 40], [0, 0]]);
        _scene.Graphical.Add([[x, 60], [x, 20]]);
        AssertNetOverlap(severity, [0, 20]);
    }

    [Theory]
    [InlineData(0, "error")]
    [InlineData(double.Epsilon, null)]
    public void NetOverlap_ZeroConfiguredSpacing_StillReportsCollinearOverlap(double y, string? severity)
    {
        _scene.Layout.ParallelSpacingMm = 0;
        ParallelWires(y);
        AssertNetOverlap(severity);
    }

    [Theory]
    [InlineData(39.99999999999999, "warning")]
    [InlineData(40, null)]
    [InlineData(40.00000000000001, null)]
    public void NetOverlap_ProjectedEndpoints_RequirePositiveCommonLength(double start, string? severity)
    {
        _scene.Graphical.Add([[0, 0], [40, 0]]);
        _scene.Graphical.Add([[start, 1], [60, 1]]);
        AssertNetOverlap(severity);
    }

    [Theory]
    [InlineData(20, -20, 20, 20)]
    [InlineData(20, 0, 60, double.Epsilon)]
    public void NetOverlap_NonparallelSegments_AreSkipped(double x1, double y1, double x2, double y2)
    {
        _scene.Graphical.Add([[0, 0], [40, 0]]);
        _scene.Graphical.Add([[x1, y1], [x2, y2]]);
        AssertNetOverlap(null);
    }

    [Fact]
    public void NetOverlap_CollinearDiagonalSegments_AreSkipped()
    {
        _scene.Graphical.Add([[0, 0], [40, 40]]);
        _scene.Graphical.Add([[20, 20], [60, 60]]);
        AssertNetOverlap(null);
    }

    [Fact]
    public void NetOverlap_SharedEndpointThroughThirdWire_ExemptsWholeNet()
    {
        ParallelWires(1);
        _scene.Graphical.Add([[0, 0], [20, 1]]);
        AssertNetOverlap(null);
    }

    [Fact]
    public void NetOverlap_ReversedDuplicateWire_IsSkipped()
    {
        _scene.Graphical.Add([[0, 0], [40, 0]]);
        _scene.Graphical.Add([[40, 0], [0, 0]]);
        AssertNetOverlap(null);
    }

    [Fact]
    public void NetOverlap_MultipleConflictingSegmentsOnSameTwoRoutes_ReportsEachRuleOnce()
    {
        _scene.Graphical.Add([[0, 0], [40, 0]]);
        _scene.Graphical.Add([[50, 0], [10, 0], [10, 1], [30, 1], [30, 2], [50, 2]]);
        JArray issues = Review("net_overlap");
        Assert.Collection(issues, issue => Check(issue, "R5:", "error"), issue => Check(issue, "R6:", "warning"));

        static void Check(JToken issue, string rule, string severity)
        {
            Assert.Equal(("net_overlap", severity, Page), ((string?)issue["code"], (string?)issue["severity"], (string?)issue["page"]));
            Assert.StartsWith(rule, (string?)issue["message"], StringComparison.Ordinal);
            Assert.Empty(Assert.IsType<JArray>(issue["objects"]));
            Assert.Equal(new double[] { 20, 0 }, issue["position"]!.ToObject<double[]>());
        }
    }

    [Fact]
    public void NetOverlap_TwoDistinctConflictingPairs_ReportsBothPairs()
    {
        ParallelWires(1);
        _scene.Graphical.Add([[0, 20], [40, 20]]);
        _scene.Graphical.Add([[20, 21], [60, 21]]);
        JArray issues = Review("net_overlap");
        Assert.Equal(new double[] { 0, 20 }, issues.Select(i => (double)i["position"]![1]!));
        Assert.All(issues, issue =>
        {
            Assert.Equal(("net_overlap", "warning", Page), ((string?)issue["code"], (string?)issue["severity"], (string?)issue["page"]));
            Assert.Empty(Assert.IsType<JArray>(issue["objects"]));
        });
    }

    [Theory]
    [InlineData("ModelView")]
    [InlineData("ProcessAndInstrumentationDiagram")]
    public void NetOverlap_NonSchematicPage_SkipsConflictingWires(string pageType)
    {
        _scene.PageType = pageType;
        ParallelWires(0);
        AssertNetOverlap(null);
    }

    [Fact]
    public void NetOverlap_CheckDisabled_IgnoresConflictingWires()
    {
        ParallelWires(0);
        Assert.Empty(Review("wire_through_symbol"));
    }

    [Theory]
    [InlineData(0, 0, "error")]
    [InlineData(0, 1, null)]
    [InlineData(double.NaN, 1, null)]
    [InlineData(double.PositiveInfinity, 1000, "warning")]
    public void NetOverlap_UnknownGrid_UsesRawGridForDefaultSpacing(double grid, double separation, string? severity)
    {
        _scene.Page.GridSize = grid;
        ParallelWires(separation);
        AssertNetOverlap(severity);
    }

    [Fact]
    public void NetOverlap_UnknownGridWithConfiguredSpacing_ReportsMeasuredGap()
    {
        _scene.Page.GridSize = 0;
        _scene.Layout.ParallelSpacingMm = 2;
        ParallelWires(1);
        AssertNetOverlap("warning");
    }

    [Fact]
    public void NetOverlap_NoMeasurableSegments_SkipsIncompleteRoutes()
    {
        _scene.Graphical.Add([[0, 0], [40, 0]]);
        _scene.Graphical.Add([]);
        _scene.Graphical.Add([[20, 0]]);
        _scene.Graphical.Add([[20, 0], [20, 0]]);
        AssertNetOverlap(null);
    }

    [Fact]
    public void NetOverlap_MissingCoordinateOnTwoPointRoute_LeavesTheSegmentOut()
    {
        _scene.Graphical.Add([[20, 0], []]);
        Assert.Empty(Review("net_overlap"));
    }
}

using EplanMcp.Coverage.Reporting;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Small")]
public sealed class FullApiCoverageTests
{
    [Fact]
    public void Build_CountsAllRoutesAndTheirLiveEvidenceAcrossKinds()
    {
        FullApiCoverage report = FullApiCoverage.Build(Members());

        Assert.Equal(10, report.Total.Denominator);
        Assert.Equal(new RouteCount(3, 2), report.Total.Routes["dedicated"]);
        Assert.Equal(new RouteCount(2, 1), report.Total.Routes["generic_read"]);
        Assert.Equal(new RouteCount(1, 1), report.Total.Routes["api_try_only"]);
        Assert.Equal(new RouteCount(1, 0), report.Total.Routes["state_only"]);
        Assert.Equal(new RouteCount(1, 1), report.Total.Routes["other"]);
        Assert.Equal(new RouteCount(2, 0), report.Total.Routes["none"]);
        Assert.Equal((4, 3, 3), (report.Methods.Denominator, report.Properties.Denominator, report.Other.Denominator));
        Assert.Equal((2, 2, 1), (Live(report.Methods), Live(report.Properties), Live(report.Other)));
    }

    [Fact]
    public void Build_EachRowPartitionsItsDenominatorAndKindsPartitionTheTotal()
    {
        FullApiCoverage report = FullApiCoverage.Build(Members());
        ApiBaseline[] kinds = [report.Methods, report.Properties, report.Other];

        Assert.All(kinds.Append(report.Total), row =>
        {
            Assert.Equal(CoverageRoutes.Names.Order(StringComparer.Ordinal), row.Routes.Keys);
            Assert.Equal(row.Denominator, row.Routes.Values.Sum(c => c.Count));
            Assert.All(row.Routes.Values, count => Assert.InRange(count.LiveVerified, 0, count.Count));
        });
        Assert.Equal(report.Total.Denominator, kinds.Sum(row => row.Denominator));
        foreach (string route in CoverageRoutes.Names)
        {
            Assert.Equal(report.Total.Routes[route].Count, kinds.Sum(row => row.Routes[route].Count));
            Assert.Equal(report.Total.Routes[route].LiveVerified, kinds.Sum(row => row.Routes[route].LiveVerified));
        }
    }

    [Fact]
    public void Build_RepeatedMembersCountEachOverload()
    {
        var overload = new FullApiMember("method", "dedicated", true);

        FullApiCoverage report = FullApiCoverage.Build([overload, overload]);

        Assert.Equal(2, report.Total.Denominator);
        Assert.Equal(new RouteCount(2, 2), report.Methods.Routes["dedicated"]);
        Assert.Equal(0, report.Properties.Denominator);
        Assert.Equal(0, report.Other.Denominator);
    }

    [Fact]
    public void Build_EmptyInputKeepsAllRowsAndRoutesAtZero()
    {
        FullApiCoverage report = FullApiCoverage.Build([]);

        Assert.All(new[] { report.Total, report.Methods, report.Properties, report.Other }, row =>
        {
            Assert.Equal(0, row.Denominator);
            Assert.Equal(6, row.Routes.Count);
            Assert.All(row.Routes.Values, count => Assert.Equal(new RouteCount(0, 0), count));
        });
    }

    [Fact]
    public void Build_UndocumentedMembersOnlyIncreaseTheUnreachableDenominators()
    {
        FullApiCoverage full = FullApiCoverage.Build([new("method", "dedicated", true)], 63, 3);

        Assert.Equal((67, 64, 3, 0), (full.Total.Denominator, full.Methods.Denominator, full.Properties.Denominator, full.Other.Denominator));
        Assert.Equal(new RouteCount(66, 0), full.Total.Routes["none"]);
        Assert.Equal(new RouteCount(63, 0), full.Methods.Routes["none"]);
        Assert.Equal(new RouteCount(3, 0), full.Properties.Routes["none"]);
        Assert.Equal(new RouteCount(1, 1), full.Total.Routes["dedicated"]);
    }

    private static int Live(ApiBaseline row) => row.Routes.Values.Sum(c => c.LiveVerified);

    private static FullApiMember[] Members() =>
    [
        new("method", "dedicated", true),
        new("method", "dedicated", true),
        new("method", "generic_read", false),
        new("method", "none", false),
        new("property", "api_try_only", true),
        new("property", "generic_read", true),
        new("property", "state_only", false),
        new("operator", "none", false),
        new("delegate_method", "dedicated", false),
        new(null, "other", true),
    ];
}

[Trait("Size", "Medium")]
public sealed class FullApiReportTests
{
    [Fact]
    public void Build_CountsOverloadsAndExcludedMembersInTheFullDenominator()
    {
        using TempRepository repository = ReportFixture.Create();

        FullApiCoverage full = CoverageReport.Build(repository.Root).FullApi!;

        Assert.Equal((6, 5, 1, 0), (full.Total.Denominator, full.Methods.Denominator, full.Properties.Denominator, full.Other.Denominator));
        Assert.Equal(new RouteCount(3, 1), full.Total.Routes["dedicated"]);
        Assert.Equal(new RouteCount(1, 0), full.Total.Routes["state_only"]);
        Assert.Equal(new RouteCount(2, 0), full.Total.Routes["none"]);
        Assert.Contains("### 全量 API", CoverageMarkdown.Render(repository.Root), StringComparison.Ordinal);
    }
}

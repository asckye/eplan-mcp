using System.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class LayoutGuardBatchTimingTests(ITestOutputHelper output)
{
    [Fact]
    public void TwoHundredExistingAndFiftyConflictingNewObjects_RefusesWithinTenSeconds()
    {
        static DrawingPlacement Row(int id, double x, double y) => new()
        {
            Kind = "Function",
            Locator = new() { Type = 21, Oid = id },
            Location = [x, y],
            Bounds = [x, y, x + 8, y + 8]
        };
        var model = new LayoutGuard.Model { Page = new() { PageSize = [840, 594], GridSize = 4 } };
        model.Page.Placements = Enumerable.Range(0, 200).Select(i => Row(i + 1, 20 + i % 20 * 32, 20 + i / 20 * 24)).ToList();
        DrawingPlacement[] proposed = [.. Enumerable.Range(0, 50).Select(i => Row(-i - 1, 20 + i % 20 * 32, 20 + i / 20 * 24))];

        var timer = Stopwatch.StartNew();
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => LayoutGuard.Enforce(model, proposed));
        timer.Stop();
        List<LayoutGuard.Violation> findings = Newtonsoft.Json.JsonConvert.DeserializeObject<List<LayoutGuard.Violation>>(error.Error.Metadata!["layoutViolations"])!;

        output.WriteLine($"LayoutGuard existing=200 new=50 all_conflicting=true elapsed_ms={timer.Elapsed.TotalMilliseconds:F3} findings={findings.Count}");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Before-write layout check took {timer.Elapsed}.");
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal(50, findings.Count);
        Assert.All(findings, finding =>
        {
            Assert.Equal("R2", finding.Rule);
            Assert.False(finding.Warning);
            Assert.Equal(2, finding.Objects.Length);
            Assert.Equal(-finding.Objects[0].Oid, finding.Objects[1].Oid);
            Assert.Null(finding.NearestFreePosition);
            Assert.Contains("time limit", finding.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoHundredExistingAndFiftyNewObjects_ReportFullGuardDuration(bool overlapping)
    {
        static DrawingPlacement Row(int id, double x, double y) => new()
        {
            Kind = "Function",
            Locator = new() { Type = 21, Oid = id },
            Location = [x, y],
            Bounds = [x, y, x + 8, y + 8]
        };
        var model = new LayoutGuard.Model { Page = new() { PageSize = [840, 594], GridSize = 4 } };
        model.Page.Placements = Enumerable.Range(0, 200).Select(i => Row(i + 1, 20 + i % 20 * 32, 20 + i / 20 * 24)).ToList();
        DrawingPlacement[] proposed = [.. Enumerable.Range(0, 50).Select(i =>
            Row(-i - 1, 20 + i % 20 * 32, (overlapping && i == 0 ? 20 : 300) + i / 20 * 24))];
        var timer = Stopwatch.StartNew();
        List<LayoutGuard.Violation> findings = LayoutGuard.Check(model, proposed);
        timer.Stop();
        output.WriteLine($"LayoutGuard existing=200 new=50 overlapping={overlapping} elapsed_ms={timer.Elapsed.TotalMilliseconds:F3} findings={findings.Count}");
        if (overlapping)
        { Assert.Contains(findings, finding => finding.Rule == "R2" && !finding.Warning); }
        else
        { Assert.Empty(findings); }
    }
}

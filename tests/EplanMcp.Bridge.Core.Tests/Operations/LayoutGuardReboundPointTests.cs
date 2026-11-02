using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardReboundPointTests
{
    private static readonly (long Oid, long SegmentOid, double[] Bounds)[] _loggedPoints =
    [
        (69919, -21, [86.75, 158.75, 89.25, 161.25]),
        (69920, -23, [118.75, 158.75, 121.25, 161.25]),
        (69921, -25, [150.75, 158.75, 153.25, 161.25]),
        (69922, -27, [182.75, 158.75, 185.25, 161.25]),
        (69923, -29, [214.75, 158.75, 217.25, 161.25]),
        (69924, -31, [246.75, 158.75, 249.25, 161.25]),
        (69926, -38, [278.75, 90.75, 281.25, 93.25])
    ];

    [Theory]
    [InlineData("no connection lists it")]
    [InlineData("pins unresolved 17/56/0/17/80/0")]
    [InlineData("no drawn path")]
    [InlineData("drawn path not unique (2 paths)")]
    public void FifthRun_ReboundExistingPointsWithoutRoutes_WarnOncePerPointThroughResultSummary(string reason)
    {
        var scene = new ConnectionCreatePlanner.Scene
        {
            Page = new() { GridSize = 4, PageSize = [420, 297] },
            Layout = new() { Strict = true }
        };
        foreach (var (oid, _, bounds) in _loggedPoints)
        {
            double x = (bounds[0] + bounds[2]) / 2, y = (bounds[1] + bounds[3]) / 2;
            scene.Page.Placements.Add(new()
            {
                Kind = "ConnectionDefinitionPoint",
                Locator = new() { Type = 59, Oid = oid },
                Bounds = bounds,
                // Location deliberately differs: measured bounds determine the annotation's centre.
                Location = [x + 4, y],
                Properties = new() { ["binding"] = "earlier call" }
            });
            scene.Graphical.Add([[x, y - 32], [x, y + 32]]);
            scene.PointRouteGaps[FormattableString.Invariant($"59/{oid}")] = reason;
        }
        LayoutGuard.Model before = LayoutGuard.From(JsonConvert.DeserializeObject<ConnectionCreatePlanner.Scene>(JsonConvert.SerializeObject(scene))!);
        foreach (DrawingPlacement point in scene.Page.Placements)
        { point.Properties["binding"] = "mixed cable rebinding"; }
        LayoutGuard.Model actual = LayoutGuard.From(scene);
        foreach (var (entry, logged) in actual.Lines.Zip(_loggedPoints))
        { entry.Locator.Oid = logged.SegmentOid; }
        // Multiple measured segments through a point still produce only one diagnostic for that point.
        actual.Lines.AddRange(actual.Lines.Select(line => new LayoutGuard.Segment { Points = [line.Points[0], [line.Points[0][0], line.Points[0][1] + 32]] }).ToArray());
        List<LayoutGuard.Violation> checkedLayout = LayoutGuard.Check(actual, actual.Page.Placements, false);
        Assert.DoesNotContain(checkedLayout, v => !v.Warning);
        Assert.Equal(7, checkedLayout.Count);
        var plan = new JObject { ["warning"] = "earlier stage warning" };
        for (int stage = 0; stage < 2; stage++)
        { plan["warning"] = LayoutGuard.Summary(new[] { (string?)plan["warning"] }.Concat(LayoutGuard.AfterWriting(before, actual))); }
        string warning = (string)plan["warning"]!;
        Assert.Contains("earlier stage warning", warning, StringComparison.Ordinal);
        foreach (var (oid, _, bounds) in _loggedPoints)
        {
            LayoutGuard.Violation diagnostic = Assert.Single(checkedLayout, v => v.Objects[0].Oid == oid);
            Assert.Equal("R4", diagnostic.Rule);
            Assert.True(diagnostic.Warning);
            Assert.Equal(bounds, diagnostic.Geometry);
            string message = FormattableString.Invariant($"R4 unverified: connection definition point 59/{oid} has no readable owning route ({reason})");
            Assert.Equal(message, diagnostic.Message);
            Assert.Single(warning.Split('\n'), line => line == message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutlineClipMissingCentre_RefusesWithOrWithoutOwningRoute(bool knownRoute)
    {
        var model = LoggedModel();
        if (knownRoute)
        { model.PointRoutes["59/69919"] = [[[88, 128], [88, 192]]]; }
        model.Lines = [new() { Points = [[89, 128], [89, 192]], Changed = true }];
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && !v.Warning);
    }

    [Fact]
    public void CentrePassage_KnownPerpendicularRouteOrUnrelatedFunction_StillRefuses()
    {
        var model = LoggedModel();
        model.Page.Placements[0].Location = [92, 160];
        model.PointRoutes["59/69919"] = [[[88, 128], [88, 192]]];
        model.Lines = [new() { Points = [[88, 128], [88, 192]], Changed = true }];
        Assert.DoesNotContain(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
        model.Lines = [new() { Points = [[80, 160], [96, 160]], Changed = true }];
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && !v.Warning);
        model.PointRoutes.Clear();
        model.Page.Placements[0].Kind = "Function";
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && !v.Warning);
    }

    [Fact]
    public void ConnectionPlan_UnknownRouteWarningIsReturnedInPlanWarning()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.Placements.Add(new()
        {
            Kind = "ConnectionDefinitionPoint",
            Locator = new() { Type = 59, Oid = 69919 },
            Location = [16, 8],
            Bounds = [14.75, 6.75, 17.25, 9.25]
        });
        fixture.Scene.PointRouteGaps["59/69919"] = "no connection lists it";
        Assert.Contains("R4 unverified: connection definition point 59/69919 has no readable owning route (no connection lists it)",
            (string?)fixture.Plan()["warning"], StringComparison.Ordinal);
    }

    [Fact]
    public void PointRouteGapReasonChanges_PlanFingerprintBecomesStale()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.PointRouteGaps["59/69919"] = "no connection lists it";
        string fingerprint = (string)fixture.Plan()["fingerprint"]!;
        fixture.Scene.PointRouteGaps["59/69919"] = "no drawn path";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => fixture.Plan(fingerprint)).Error.Reason);
    }

    private static LayoutGuard.Model LoggedModel() => new()
    {
        Page = new()
        {
            GridSize = 4,
            PageSize = [420, 297],
            Placements = [new()
            {
                Kind = "ConnectionDefinitionPoint", Locator = new() { Type = 59, Oid = 69919 },
                Location = [88, 160], Bounds = [86.75, 158.75, 89.25, 161.25]
            }]
        }
    };
}

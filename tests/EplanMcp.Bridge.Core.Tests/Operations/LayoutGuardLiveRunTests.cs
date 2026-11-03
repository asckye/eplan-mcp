using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardLiveRunTests
{
    [Theory]
    [InlineData(69919, 88, -21)]
    [InlineData(69920, 120, -23)]
    [InlineData(69921, 152, -25)]
    public void LoggedType59_OwnRouteThroughOrEndingAtDefinitionPoint_IsAllowed(long oid, double x, long segmentOid)
    {
        DrawingPlacement point = new()
        {
            Kind = "ConnectionDefinitionPoint",
            Locator = new() { Type = 59, Oid = oid },
            Location = [x, 160],
            Bounds = [x - 1.25, 158.75, x + 1.25, 161.25]
        };
        var scene = new ConnectionCreatePlanner.Scene
        {
            Page = new() { GridSize = 4, PageSize = [420, 297], Placements = [point] },
            PointRoutes = new() { [FormattableString.Invariant($"59/{oid}")] = [[[x, 128], [x, 192]]] }
        };
        LayoutGuard.Model model = LayoutGuard.From(scene);
        foreach (double startY in new[] { 128d, 160d })
        {
            model.Lines = [new() { Locator = new() { Oid = segmentOid }, Points = [[x, startY], [x, 192]], Changed = true }];
            Assert.DoesNotContain(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
            // AfterWriting uses measured points and retains ownership even after assigning connected-net names.
            Assert.NotEmpty(LayoutGuard.AfterWriting(new() { Page = new() { GridSize = 4, PageSize = [420, 297] } }, model));
        }
        model.Lines = [new() { Points = [[x - 8, 160], [x + 8, 160]], Changed = true }];
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && !v.Warning);
        model.Lines = [new() { Points = [[x + 1, 128], [x + 1, 192]], Changed = true }];
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && !v.Warning);
        model.Lines = [new() { Points = [[x, 128], [x, 192]], Changed = true }];
        model.PointRoutes.Clear();
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && v.Warning);
        model.PointRoutes[FormattableString.Invariant($"59/{oid + 1}")] = [[[x, 128], [x, 192]]];
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && v.Warning);
        point.Kind = "Function";
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4" && !v.Warning);
    }

    [Theory]
    [InlineData(88)]
    [InlineData(120)]
    [InlineData(152)]
    public void RoutingNode_OnlyOwnEndpointPinsAndSupportedDirectionsAllowApproach(double x)
    {
        var model = new LayoutGuard.Model { Page = new() { GridSize = 4, PageSize = [420, 297] } };
        DrawingPlacement node = new()
        {
            Kind = "SymbolReference",
            Locator = new() { Type = 13, Oid = 1 },
            Location = [x, 160],
            Bounds = [x - 1.25, 158.75, x + 1.25, 161.25]
        };
        model.Page.Placements = [node];
        model.Pins = new()
        {
            ["13/1/0"] = new() { Location = [x, 160], Direction = "Up" },
            ["13/1/1"] = new() { Location = [x, 160], Direction = "Down" }
        };
        model.Lines = [new() { Points = [[x, 160], [x, 192]], Changed = true }];
        Assert.DoesNotContain(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
        model.Lines[0].Points = [[x, 128], [x, 192]];
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
        model.Lines[0].Points = [[x, 160], [x, 192]];
        model.Pins["13/1/0"].Direction = "Left";
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
        node.Locator.Oid = 2;
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
    }

    [Fact]
    public void LiveTNode_ThreeDirectedPinApproachesAreAllowed_ThroughOutlineAndWrongDirectionsRefuse()
    {
        var scene = TNodeFixture.Scene();
        DrawingPlacement node = scene.Page.Placements[0];
        foreach (double[][] segment in scene.Graphical)
        {
            Assert.False(LayoutGuard.Crosses(segment, node, scene.Pins));
            Assert.False(LayoutGuard.Crosses(segment.Reverse().ToArray(), node, scene.Pins));
        }
        Assert.True(LayoutGuard.Crosses([[120, 100], [120, 220]], node, scene.Pins));
        Assert.True(LayoutGuard.Crosses([[100, 160], [200, 160]], node, scene.Pins));
        Assert.True(LayoutGuard.Crosses([[122, 160], [122, 220]], node, scene.Pins));
        Assert.True(LayoutGuard.Crosses([[122, 160], [100, 160]], node, scene.Pins));
        scene.Pins["13/423458/2"].Direction = "Left";
        Assert.True(LayoutGuard.Crosses(scene.Graphical[2], node, scene.Pins));
        node.Locator.Oid++;
        Assert.True(LayoutGuard.Crosses(scene.Graphical[0], node, scene.Pins));
    }

    [Fact]
    public void Logged69863ZeroBounds_MoveDownTwenty_RemainUnknown()
    {
        var fixture = new MoveFixture();
        fixture.Scene.Page.PageSize = [420, 297];
        fixture.Scene.Page.GridSize = 4;
        fixture.Root.Locator.Oid = 69863;
        fixture.Root.Location = [80, 160];
        fixture.Root.Bounds = [78, 156, 82, 164];
        DrawingPlacement child = fixture.Child('b', [80, 160]);
        child.Locator.Oid = 69863;
        child.Bounds = [0, 0, 0, 0];
        fixture.Request["moves"] = new JArray(MoveFixture.Move(fixture.Root.Locator, [0, -20]));
        Assert.Equal([0d, 0d, 0d, 0d], LayoutGuard.Shift(child, [0, -20]).Bounds!);
        Assert.Equal([80d, 140d], fixture.Plan()["moves"]![0]!["after"]!.Values<double>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownPosition_IsNeitherChangedViolationNorObstacle(bool zeroBounds)
    {
        var model = LayoutGuardTests.Model();
        model.Config.Strict = true;
        model.SetsTextAttribute = (_, _) => true;
        DrawingPlacement unknown = LayoutGuardTests.Row("Text", 1);
        unknown.Location = [];
        unknown.Bounds = zeroBounds ? [0, 0, 0, 0] : [40, 40, 44, 44];
        unknown.Properties["angle"] = "45";
        unknown.Properties["cross_reference"] = "true";
        Assert.Empty(LayoutGuard.Check(model, [unknown]));
        model.Page.Placements = [unknown];
        model.Lines = [new() { Points = [[20, 42], [60, 42]], Changed = true }];
        Assert.DoesNotContain(LayoutGuard.Check(model, [LayoutGuardTests.Row("Text", 2)], false), v => v.Objects.Any(o => o.Oid == 1));
        var actual = LayoutGuardTests.Model();
        actual.Page.Placements = [unknown];
        Assert.Contains(LayoutGuard.AfterWriting(LayoutGuardTests.Model(), actual), s => s.Contains("unknown position", StringComparison.Ordinal));
    }

    [Fact]
    public void Logged69874And69873_SameCardOutline_UsesOwnershipRatherThanEqualBounds()
    {
        var model = new LayoutGuard.Model { Page = new() { PageSize = [420, 297], GridSize = 4 } };
        DrawingPlacement a = LayoutGuardTests.Row("Function", 69874, 88, 44), b = LayoutGuardTests.Row("Function", 69873, 88, 44);
        a.Bounds = b.Bounds = [68, 36, 108, 65.25];
        a.Properties["layout_container_owner"] = b.Properties["layout_container_owner"] = "33/45/10232/0";
        DrawingPlacement text = LayoutGuardTests.Row("PropertyPlacement", 69873, 88, 44);
        text.Parent = b.Locator;
        model.Page.Placements = [b, text];
        Assert.DoesNotContain(LayoutGuard.Check(model, [a], false), v => v.Rule is "R2" or "R3");
        a.Bounds = [72, 40, 104, 60];
        Assert.DoesNotContain(LayoutGuard.Check(model, [a], false), v => v.Rule is "R2" or "R3");
        a.Properties["layout_container_owner"] = "33/45/10233/0";
        Assert.Contains(LayoutGuard.Check(model, [a], false), v => v.Rule == "R2" && !v.Warning);
        a.Properties.Remove("layout_container_owner");
        a.Bounds = b.Bounds;
        Assert.Contains(LayoutGuard.Check(model, [a], false), v => v.Rule == "R2" && !v.Warning);
    }

    [Fact]
    public void SameParentBox_MembersAndTheirTexts_DoNotConflictWithEachOther()
    {
        var model = LayoutGuardTests.Model();
        DrawingPlacement box = LayoutGuardTests.Row("Box", 10);
        DrawingPlacement a = LayoutGuardTests.Row("Function", 1), b = LayoutGuardTests.Row("Function", 2);
        a.Parent = b.Parent = box.Locator;
        DrawingPlacement text = LayoutGuardTests.Row("PropertyPlacement", 2);
        text.Locator.Digest = new string('d', 64);
        text.Parent = b.Locator;
        model.Page.Placements = [box, b, text];
        Assert.DoesNotContain(LayoutGuard.Check(model, [a], false), v => v.Rule is "R2" or "R3");
        a.Parent = null;
        Assert.Contains(LayoutGuard.Check(model, [a], false), v => v.Rule == "R2" && !v.Warning);
    }

    [Fact]
    public void ConnectionPlanner_UnknownPositionIsDeferred_KnownObstacleStillRefuses()
    {
        var fixture = new ConnectionCreateFixture();
        var obstacle = new DrawingPlacement { Locator = new() { Type = 17, Oid = 3 }, Kind = "Function", Bounds = [10, 4, 14, 12] };
        fixture.Scene.Page.Placements.Add(obstacle);
        Assert.Equal(2, ((JArray)fixture.Plan()["graphicalAutoconnect"]!).Count);
        obstacle.Location = [12, 8];
        Assert.Contains("R4", Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasuredRefusal_SearchMovesWholeOwnerGroup_OrExplicitlyReportsExhaustion(bool oversized)
    {
        var before = LayoutGuardTests.Model();
        DrawingPlacement owner = LayoutGuardTests.Row("Function", 3, 40, 40);
        DrawingPlacement text = LayoutGuardTests.Row("PropertyPlacement", 3, 80, 40);
        // ContextPropertyPlacement has its own digest, and the observed parent digest may differ.
        text.Locator.Digest = new string('b', 64);
        text.Parent = new() { Type = 17, Oid = 3, Digest = new string('a', 64) };
        text.Bounds = oversized ? [80, 40, 200, 44] : [80, 40, 100, 44];
        DrawingPlacement empty = LayoutGuardTests.Row("PropertyPlacement", 3, 40, 40);
        empty.Locator.Digest = new string('c', 64);
        empty.Parent = text.Parent;
        empty.Bounds = [0, 0, 0, 0];
        var actual = LayoutGuardTests.Model();
        actual.Page.Placements = [owner, text, empty];
        if (oversized)
        {
            // ADR-0046 S2: past the page edge the write still rolls back.
            BridgeError error = Assert.Throws<BridgeErrorException>(() => LayoutGuard.AfterWriting(before, actual)).Error;
            LayoutGuard.Violation failure = Assert.Single(JsonConvert.DeserializeObject<List<LayoutGuard.Violation>>(error.Metadata!["layoutViolations"])!, v => !v.Warning);
            Assert.Equal("R1", failure.Rule);
            Assert.Null(failure.NearestFreePosition);
            Assert.Contains("No free position found within the bounded search", error.Message, StringComparison.Ordinal);
        }
        else
        {
            // Inside the page but outside the margin it is a committed deviation that keeps the suggestion.
            Assert.Contains(LayoutGuard.AfterWriting(before, actual), s => s.StartsWith("Readback deviation: R1", StringComparison.Ordinal)
                && s.Contains("Suggested position: [28, 40] mm", StringComparison.Ordinal));
            Assert.Empty(LayoutGuard.Check(actual, actual.Page.Placements.Select(p => LayoutGuard.Shift(p, [-12, 0])).ToArray(), false, true));
        }
        Assert.Equal([40d, 40d], owner.Location);
        Assert.Equal([80d, 40d], text.Location);
        Assert.Equal([0d, 0d, 0d, 0d], empty.Bounds);
    }
}

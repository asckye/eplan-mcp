using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageReviewRulesTests
{
    [Fact]
    public void OverlapFix_AndIssueObjects_UsePublishedLowercaseLocatorFields()
    {
        Placement(1, kind: "Text");
        Placement(2, 9, "Text");
        JToken issue = Assert.Single(Review("overlaps"));
        Assert.Equal(["project", "page", "type", "oid", "digest"], ((JObject)issue["objects"]![0]!).Properties().Select(p => p.Name));
        Assert.Equal(2, (long)issue["fix"]!["args"]!["moves"]![0]!["locator"]!["oid"]!);
        Assert.Null(issue["fix"]!["args"]!["moves"]![0]!["locator"]!["Oid"]);
    }

    /// <summary>Agent eval 2026-11-06: PLC boxes 24 mm apart touch; EPLAN read one edge as 235.9999999999999.</summary>
    [Fact]
    public void Overlap_TouchingWithFloatingPointNoise_IsNotReported()
    {
        DrawingPlacement upper = Placement(1, 8, "Function", "-K1");
        DrawingPlacement lower = Placement(2, 8, "Function", "-K2");
        upper.Bounds = [0, 235.9999999999999, 10, 260];
        lower.Bounds = [0, 212, 10, 236];
        Assert.Empty(Review("overlaps"));
    }

    [Theory]
    [InlineData(-20, false)]   // inside the box
    [InlineData(-36, true)]    // across its left edge
    [InlineData(-60, false)]   // outside
    public void TextAcrossALocationBoxBorder_IsReported(double left, bool reported)
    {
        DrawingPlacement box = Placement(1, 100, "LocationBox");
        box.Bounds = [70, 36, 160, 108];
        DrawingPlacement text = Placement(2, 100 + left, "PropertyPlacement");
        text.Bounds = [100 + left, 96, 100 + left + 30, 100];
        JArray issues = Review("box_crossings");
        Assert.Equal(reported ? 1 : 0, issues.Count);
        if (reported)
        { Assert.Equal(("text_crosses_box", "warning"), ((string?)issues[0]!["code"], (string?)issues[0]!["severity"])); }
    }

    [Fact]
    public void TheBoxOwnLabelOnItsCorner_IsNotReported()
    {
        DrawingPlacement box = Placement(1, 100, "LocationBox");
        box.Bounds = [70, 36, 160, 108];
        DrawingPlacement label = Placement(2, 66, "PropertyPlacement");
        label.Bounds = [64, 104, 74, 110];
        label.Parent = box.Locator;
        Assert.Empty(Review("box_crossings"));
    }

    /// <summary>A145: the sample's A3 frame grid is [4, 16, 420, 292]; the title block lies below y 16.</summary>
    [Theory]
    [InlineData(100, 100, false, 0, 0)]  // inside
    [InlineData(100, 14, true, 100, 20)] // into the title block: moved up onto the grid
    [InlineData(419, 100, true, 416, 100)] // past the right edge: moved left
    public void OutlineLeavingTheFrameArea_IsAnErrorWithAMoveInside(double x, double y, bool reported, double fixX, double fixY)
    {
        _scene.Page.PageSize = [420, 297];
        _scene.Page.DrawingArea = [4, 16, 420, 292];
        DrawingPlacement p = Placement(1, x, "Function", "-K1");
        p.Location = [x, y];
        p.Bounds = [x - 2, y - 2, x + 2, y + 2];
        JArray issues = Review("off_area");
        Assert.Equal(reported ? 1 : 0, issues.Count);
        if (reported)
        {
            Assert.Equal(("off_area", "error"), ((string?)issues[0]!["code"], (string?)issues[0]!["severity"]));
            JToken move = issues[0]!["fix"]!["args"]!["moves"]![0]!;
            Assert.Equal((fixX, fixY), ((double)move["x_mm"]!, (double)move["y_mm"]!));
        }
    }

    [Fact]
    public void WithoutAFrameArea_OnlyThePageEdgeCounts()
    {
        _scene.Page.PageSize = [420, 297];
        _scene.Graphical.Add([[8, 8], [8, 300]]);
        DrawingPlacement p = Placement(1, 8, "Function", "-K1");
        p.Bounds = [6, 6, 10, 10];
        JToken issue = Assert.Single(Review("off_area"));
        // Live 2026-11-16: wire points off the page are not drawn and may be unreadable coordinates.
        Assert.Equal("warning", (string?)issue["severity"]);
        _scene.Page.DrawingArea = [4, 16, 416, 292];
        p.Bounds = [6, 20, 10, 24];
        _scene.Graphical[0] = [[8, 20], [8, 300]];
        Assert.Equal("warning", (string?)Assert.Single(Review("off_area"))["severity"]);
        _scene.Graphical[0] = [[8, 8], [8, 100]];
        Assert.Equal(("error", "Wire point leaves the plot frame's drawing area; reroute it inside [4, 16, 416, 292]."),
            ((string?)Assert.Single(Review("off_area"))["severity"], (string?)Assert.Single(Review("off_area"))["message"]));
    }

    [Fact]
    public void SymbolOnSymbol_IsAnError_TextOnText_StaysAWarning()
    {
        Placement(1, 8, "Function", "-K1").Bounds = [4, 4, 12, 12];
        DrawingPlacement k2 = Placement(2, 9, "Function", "-K2");
        k2.Bounds = [8, 8, 16, 16];
        Assert.Equal("error", (string?)Assert.Single(Review("overlaps"))["severity"]);
        k2.Bounds = [11.75, 8, 16, 16]; // touching by 0.25 mm, like the official mated plugs
        Assert.Equal("warning", (string?)Assert.Single(Review("overlaps"))["severity"]);
        _scene.Page.Placements.Clear();
        Placement(3, 8, "Text");
        Placement(4, 9, "Text");
        Assert.Equal("warning", (string?)Assert.Single(Review("overlaps"))["severity"]);
    }

    /// <summary>Owner 2026-11-16: an overlap error carries the nearest free grid position for the second symbol.</summary>
    [Fact]
    public void SymbolOnSymbol_FixMovesToTheNearestFreeGridPosition()
    {
        DrawingPlacement k1 = Placement(1, 40, "Function", "-K1");
        k1.Location = [40, 40];
        k1.Bounds = [36, 36, 44, 44];
        DrawingPlacement k2 = Placement(2, 44, "Function", "-K2");
        k2.Location = [44, 40];
        k2.Bounds = [40, 36, 48, 44];
        JToken issue = Assert.Single(Review("overlaps"));
        JToken move = issue["fix"]!["args"]!["moves"]![0]!;
        Assert.Equal(2L, (long)move["locator"]!["oid"]!);
        double x = (double)move["x_mm"]!, y = (double)move["y_mm"]!;
        Assert.Equal((0, 0), (x % 4, y % 4));
        double[] moved = [x - 4, y - 4, x + 4, y + 4];
        // eplan_placement_update refuses outlines that even touch (live 2026-11-16), so the suggestion keeps clear of the edge too.
        Assert.False(moved[0] <= 44 && moved[2] >= 36 && moved[1] <= 44 && moved[3] >= 36, $"[{x}, {y}] still touches -K1");
        Assert.Contains("nearest free position", (string?)issue["message"], StringComparison.Ordinal);
    }

    /// <summary>Live 2026-11-16: the official -TB1 device symbol encloses its connection points -TB1:13 and -TB1:+.</summary>
    [Fact]
    public void ADeviceSymbolOverItsOwnConnectionPoints_IsAWarning()
    {
        DrawingPlacement device = Placement(1, 44, "Function", "=GAB1+A2-TB1");
        device.Bounds = [44, 136, 160, 176];
        DrawingPlacement point = Placement(2, 140, "Function", "=GAB1+A2-TB1:13");
        point.Bounds = [139, 135.75, 141, 142.25];
        Assert.Equal("warning", (string?)Assert.Single(Review("overlaps"))["severity"]);
    }

    /// <summary>Live 2026-11-16: the pins of the sub-device -KK1-X11 sit 6 mm deep on the -KK1 box edge.</summary>
    [Fact]
    public void ABoxOverItsSubDevicePins_IsAWarning()
    {
        DrawingPlacement box = Placement(1, 44, "BoxedDevice", "=EC1+A3-EC1-KK1");
        box.Bounds = [44, 236, 336, 300];
        DrawingPlacement pin = Placement(2, 308, "Function", "=EC1+A3-EC1-KK1-X11:1");
        pin.Bounds = [306.25, 235.75, 309.75, 242.25];
        Assert.Equal("warning", (string?)Assert.Single(Review("overlaps"))["severity"]);
    }

    /// <summary>Live 2026-11-16: a wire runs through the connection point of a small DCPOL symbol.</summary>
    [Fact]
    public void AWireThroughTheSymbolsOwnPin_DoesNotCrossIt()
    {
        DrawingPlacement point = Placement(1, 292, "Function", "-QA1");
        point.Location = [292, 380];
        point.Bounds = [291.75, 377.25, 292.25, 380.25];
        _scene.Pins["13/1/0"] = new() { Location = [292, 380] };
        _scene.Graphical.Add([[292, 360], [292, 400]]);
        Assert.Empty(Review("wire_through_symbol"));
    }

    /// <summary>Live 2026-11-16: a PLC plug outline covers its neighbour's pin, where the wire ends.</summary>
    [Fact]
    public void AWireEndingOnAPinInsideAnOutline_DoesNotCrossIt()
    {
        DrawingPlacement plug = Placement(1, 32, "Function", "-KE4:1.1");
        plug.Location = [32, 228];
        plug.Bounds = [21.75, 223.75, 66.25, 258.25];
        Placement(2, 44, "Function", "-KE4:1.2").Bounds = [43, 240, 45, 242];
        _scene.Pins["13/2/0"] = new() { Location = [44, 241] };
        _scene.Graphical.Add([[44, 241], [44, 300]]);
        Assert.Empty(Review("wire_through_symbol"));
    }

    [Fact]
    public void WireAcrossASymbol_IsAnError()
    {
        DrawingPlacement k1 = Placement(1, 40, "Function", "-K1");
        k1.Location = [40, 40];
        k1.Bounds = [36, 32, 44, 48];
        _scene.Graphical.Add([[20, 40], [60, 40]]);
        JToken issue = Assert.Single(Review("wire_through_symbol"));
        Assert.Equal(("wire_through_symbol", "error", 1L), ((string?)issue["code"], (string?)issue["severity"], (long)issue["objects"]![0]!["oid"]!));
        Assert.Equal("eplan_connection_create", (string?)issue["fix"]!["tool"]);
    }

    [Theory]
    [InlineData(new double[] { 20, 60, 60, 60 }, "error")]    // two nets on one line
    [InlineData(new double[] { 20, 62, 60, 62 }, "warning")]  // half a grid apart
    [InlineData(new double[] { 40, 60, 80, 60 }, null)]       // one net: the wires meet at x 40
    public void ParallelWiresOfDifferentNets_AreReportedOnce(double[] other, string? severity)
    {
        _scene.Graphical.Add([[0, 60], [40, 60]]);
        _scene.Graphical.Add([[other[0], other[1]], [other[2], other[3]]]);
        JArray issues = Review("net_overlap");
        if (severity is null)
        { Assert.Empty(issues); }
        else
        { Assert.Equal(severity, (string?)Assert.Single(issues)["severity"]); }
    }

    [Fact]
    public void TinyOrSlantedText_IsAWarningWithAMoveFix()
    {
        DrawingPlacement text = Placement(1, 100, "Text");
        text.Properties["height_mm"] = "0.3";
        text.Properties["angle"] = "45";
        JToken issue = Assert.Single(Review("text_format"));
        Assert.Equal(("text_format", "warning", "eplan_placement_update"), ((string?)issue["code"], (string?)issue["severity"], (string?)issue["fix"]!["tool"]));
    }

    private const string Page = "=F+A1/1";
    private readonly ConnectionCreatePlanner.Scene _scene = new() { Project = new() { Name = "Line1" }, Page = new() { Page = Page, GridSize = 4 } };
    private readonly JObject _actual = JObject.Parse("""{"devices":[],"connections":[],"cables":[],"interruptions":[],"texts":[],"messages":[]}""");
    private JArray Review(string check, JObject? expected = null) => PageReviewRules.Check(_scene, _actual, new() { ["checks"] = new JArray(check), ["expected"] = expected });
    private DrawingPlacement Placement(long oid, double x = 8, string kind = "Function", string? name = null)
    {
        var p = new DrawingPlacement { Locator = new() { Project = "Line1", Page = Page, Oid = oid, Type = 13 }, Location = [x, 8], Bounds = [x - 1, 7, x + 1, 9], Kind = kind };
        if (name is not null)
        { p.Properties["name"] = name; }
        _scene.Page.Placements.Add(p);
        return p;
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DanglingEnds_RequiresPinAtEachEnd(bool attached)
    {
        _scene.Graphical.Add([[8, 8], [8, 24]]);
        _scene.Pins["13/1/0"] = new() { Location = [8, 8] };
        if (attached)
        { _scene.Pins["13/2/0"] = new() { Location = [8, 24] }; }
        Assert.Equal(attached ? 0 : 1, Review("dangling_ends").Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoggedDanglingFixture_OriginIsSkipped_RealEndRequiresRepair(bool repaired)
    {
        // A73: dynamicLines/423455 persists after repair; the segment supplies the actual BAT pin positions.
        var scene = new ConnectionCreatePlanner.Scene { Project = new() { Name = "LargeWrite" }, Page = new() { Page = "=GAA+A1&EFS1/103", GridSize = 4 } };
        scene.Graphical.Add([[0, 0], [200, 160]]);
        if (repaired)
        {
            scene.Graphical.Add([[120, 160], [200, 160]]);
            scene.Pins["17/69839/0"] = new() { Location = [120, 160], Direction = "Right" };
            scene.Pins["17/69840/0"] = new() { Location = [200, 160], Direction = "Left" };
        }
        JArray issues = PageReviewRules.Check(scene, _actual, new() { ["checks"] = new JArray("dangling_ends") });
        if (repaired)
        { Assert.Empty(issues); }
        else
        {
            JToken issue = Assert.Single(issues);
            Assert.Equal("dangling_end", (string?)issue["code"]);
            Assert.Equal("error", (string?)issue["severity"]);
            Assert.Equal(new double[] { 200, 160 }, issue["position"]!.Values<double>());
        }
    }
    [Fact]
    public void DanglingEnds_EmptyAndZeroLengthRepresentationsAreNotEnds()
    {
        _scene.Graphical.Add([]);
        _scene.Graphical.Add([[8, 8]]);
        _scene.Graphical.Add([[8, 8], [8, 8]]);
        _scene.Graphical.Add([[0, 0], [0, 0]]);
        _scene.Graphical.Add([[120, 160], [200, 160]]);
        _scene.Pins["17/1/0"] = new() { Location = [120, 160] };
        _scene.Pins["17/2/0"] = new() { Location = [200, 160] };
        Assert.Empty(Review("dangling_ends"));
        _scene.Pins.Remove("17/2/0");
        Assert.Equal(new double[] { 200, 160 }, Assert.Single(Review("dangling_ends"))["position"]!.Values<double>());
    }
    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new double[] { 8 })]
    [InlineData(new double[] { 8, 8, 8 })]
    [InlineData(new double[] { double.NaN, 8 })]
    [InlineData(new double[] { 8, double.PositiveInfinity })]
    [InlineData(new double[] { 0, 0 })]
    public void DanglingEnds_UnknownEndpointsDoNotHideKnownDanglingEnd(double[] unknown)
    {
        _scene.Graphical.Add([unknown, [200, 160]]);
        _scene.Graphical.Add([[200, 160], unknown]);
        Assert.Equal(new double[] { 200, 160 }, Assert.Single(Review("dangling_ends"))["position"]!.Values<double>());
    }
    [Fact]
    public void DanglingEnds_OnlyExactOriginIsSkipped()
    {
        _scene.Graphical.Add([[0, 0], [0, 1e-9]]);
        _scene.Graphical.Add([[0, 1e-9], [8, 8]]);
        _scene.Pins["13/1/0"] = new() { Location = [8, 8] };
        Assert.Equal(new double[] { 0, 1e-9 }, Assert.Single(Review("dangling_ends"))["position"]!.Values<double>());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DanglingEnds_SharedPolylineEndsRequirePinOrNode(bool node)
    {
        _scene.Graphical.Add([[8, 8], [8, 24]]);
        _scene.Graphical.Add([[8, 24], [24, 24]]);
        _scene.Pins["13/1/0"] = new() { Location = [8, 8] };
        _scene.Pins["13/2/0"] = new() { Location = [24, 24] };
        if (node)
        {
            // A55: a connection node's graphical pins coincide at its center.
            _scene.Pins["17/3/0"] = new() { Location = [8, 24] };
            _scene.Pins["17/3/1"] = new() { Location = [8, 24] };
        }
        JArray issues = Review("dangling_ends");
        if (node)
        { Assert.Empty(issues); }
        else
        { Assert.Equal(new double[] { 8, 24 }, Assert.Single(issues)["position"]!.Values<double>()); }
    }
    [Fact]
    public void ZeroLengthRepresentation_DoesNotConnectAnOriginPin()
    {
        Placement(1, name: "-A");
        _scene.Graphical.Add([[0, 0], [0, 0]]);
        _scene.Pins["13/1/0"] = new() { Location = [0, 0] };
        Assert.Equal("unconnected_pin", (string?)Assert.Single(Review("unconnected_pins"))["code"]);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnconnectedPin_ConnectedPinIsClean(bool connected)
    {
        Placement(1, name: "+A1-K1");
        _scene.Pins["13/1/0"] = new() { Location = [8, 8] };
        if (connected)
        { _scene.Graphical.Add([[8, 8], [8, 24]]); }
        JArray issues = Review("unconnected_pins");
        Assert.Equal(connected ? 0 : 1, issues.Count);
        if (!connected)
        { Assert.Equal("warning", (string?)issues[0]["severity"]); }
    }
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void UnconnectedPin_TwoPinInterruptionPointIsOneEnd(bool connected, int expected)
    {
        // Customer acceptance 2026-11-03: a BP point (two pins at one end) was reported once per pin.
        Placement(1, kind: "InterruptionPoint", name: "=F+A1-X4:2b");
        _scene.Pins["13/1/0"] = new() { Location = [8, 7] };
        _scene.Pins["13/1/1"] = new() { Location = [8, 8] };
        if (connected)
        { _scene.Graphical.Add([[8, 8], [8, 24]]); }
        JArray issues = Review("unconnected_pins");
        Assert.Equal(expected, issues.Count);
        if (expected == 1)
        { Assert.Equal(new double[] { 8, 8 }, issues[0]["position"]!.Values<double>()); }
    }
    [Fact]
    public void UnconnectedPin_RequiredByTarget_IsError()
    {
        Placement(1, name: "+A1-K1");
        _scene.Pins["13/1/0"] = new() { Location = [8, 8] };
        _actual["devices"] = JArray.Parse("""[{"locator":{"oid":1},"pins":{"0":"1","1":"2"}}]""");
        _scene.Pins["13/1/1"] = new() { Location = [8, 4] };
        Assert.Equal("warning", (string?)Review("unconnected_pins", JObject.Parse("""{"connections":[{"from":{"device":"+A1-K1","pin":"1"}}]}"""))[1]["severity"]);
        Assert.Equal("error", (string?)Review("unconnected_pins", JObject.Parse("""{"connections":[{"from":{"device":"+A1-K1","pin":"1"},"to":{"device":"-K2","pin":"2"}}]}"""))[0]["severity"]);
    }
    [Theory]
    [InlineData("Function", true)]
    [InlineData("Function", false)]
    [InlineData("Text", true)]
    [InlineData("Text", false)]
    public void Overlap_StrictBoundsIntersection(string kind, bool overlaps)
    {
        Placement(1, kind: kind, name: "-K1");
        Placement(2, overlaps ? 9 : 10, kind, "-K2");
        Assert.Equal(overlaps ? 1 : 0, Review("overlaps").Count);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OffGrid_UsesPlacementRatherThanLibraryPin(bool offGrid)
    {
        Placement(1, offGrid ? 8.25 : 8);
        _scene.Pins["13/1/0"] = new() { Location = [10, 7.25] };
        _scene.Graphical.Add([[10, 7.25], [12, 16]]);
        _scene.Pins["13/2/0"] = new() { Location = [12, 16] };
        Assert.Equal(offGrid ? 1 : 0, Review("off_grid").Count);
    }
    [Fact]
    public void OffGrid_WireBendIsReported_AndOtherChecksAreNotRun()
    {
        _scene.Graphical.Add([[8, 8], [8.25, 24], [24, 24]]);
        _scene.Pins["13/1/0"] = new() { Location = [8, 8] };
        _scene.Pins["13/2/0"] = new() { Location = [24, 24] };
        JToken issue = Assert.Single(Review("off_grid"));
        Assert.Equal("off_grid", (string?)issue["code"]);
        Assert.Equal("info", (string?)issue["severity"]);
        Assert.Equal(new double[] { 8.25, 24 }, issue["position"]!.Values<double>());
        Assert.Empty(Review("dangling_ends"));
    }
    private static JObject Connection(string from, string to) => new() { ["from"] = new JObject { ["device"] = from, ["pin"] = "1" }, ["to"] = new JObject { ["device"] = to, ["pin"] = "1" } };
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Junction_OnlyReportsConnectionsBetweenDistinctTargetNetworks(bool joins)
    {
        ((JArray)_actual["connections"]!).Add(Connection("-A", joins ? "-C" : "-B"));
        var target = new JObject { ["connections"] = new JArray(Connection("-A", "-B"), Connection("-C", "-D")) };
        Assert.Equal(joins ? 1 : 0, Review("unintended_junctions", target).Count);
        Assert.Empty(Review("unintended_junctions"));
    }
    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public void A65_ProjectPageOrderThenPosition_PairsAdjacentPoints(bool samePage, int count)
    {
        var points = Enumerable.Range(0, count).Select(i => new JObject { ["name"] = "I1", ["page"] = samePage || i % 2 == 0 ? Page : "=F+A1/2", ["pageOrder"] = samePage || i % 2 == 0 ? 0 : 1, ["position"] = new JArray(8 + 8 * i, 8), ["locator"] = JObject.FromObject(Placement(i + 1).Locator) }).ToArray();
        _actual["interruptions"] = new JArray(points.Reverse());
        JObject first = points[0];
        JObject peer = samePage || count == 2 ? points[1] : points[2];
        var target = new JObject { ["interruptions"] = new JArray(new JObject { ["name"] = "I1", ["position"] = first["position"]!.DeepClone(), ["counterpart"] = new JObject { ["page"] = peer["page"]!.DeepClone(), ["position"] = peer["position"]!.DeepClone() } }) };
        Assert.Empty(Review("interruption_pairing", target));
        target["interruptions"]![0]!["counterpart"]!["position"] = new JArray(500, 500);
        Assert.Equal("interruption_pairing", (string?)Assert.Single(Review("interruption_pairing", target))["code"]);
    }
    [Fact]
    public void A65_SinglePoint_OnlyFailsWhenTargetRequiresCounterpart()
    {
        _actual["interruptions"] = JArray.Parse("""[{"page":"=F+A1/1","name":"I1","pageOrder":0,"position":[8,8],"locator":{"oid":1}}]""");
        Assert.Empty(Review("interruption_pairing"));
        var target = JObject.Parse("""{"interruptions":[{"name":"I1","position":[8,8],"counterpart":{"page":"=F+A1/2","position":[8,8]}}]}""");
        Assert.Single(Review("interruption_pairing", target));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A64_TrunkSharesCoreWithUnintendedBranch_BranchDoesNot(bool trunk)
    {
        JObject selected = Connection("-A", "-B"), branch = Connection("-A", "-C");
        selected["label"] = branch["label"] = "1";
        selected["colour"] = branch["colour"] = "RD";
        var expected = new JObject { ["cables"] = new JArray(new JObject { ["designation"] = "-W1", ["cores"] = new JArray(selected.DeepClone()) }) };
        selected["definitionPoints"] = new JArray(42);
        branch["definitionPoints"] = new JArray(trunk ? 42 : 43);
        _actual["cables"] = new JArray(new JObject { ["designation"] = "-W1", ["cores"] = trunk ? new JArray(selected, branch) : new JArray(selected) });
        Assert.Equal(trunk ? 1 : 0, Review("cable_cores", expected).Count);
        Assert.Empty(Review("cable_cores"));
    }
    [Fact]
    public void CableSharedDefinitionPoint_IsCheckedAgainstAllPageConnections()
    {
        JObject selected = Connection("-A", "-B"), branch = Connection("-A", "-C");
        selected["label"] = "1";
        selected["colour"] = "RD";
        var expected = new JObject { ["cables"] = new JArray(new JObject { ["designation"] = "-WReview103", ["cores"] = new JArray(selected.DeepClone()) }) };
        selected["definitionPoints"] = new JArray(42);
        branch["definitionPoints"] = new JArray(42);
        _actual["connections"] = new JArray(selected, branch);
        _actual["cables"] = new JArray(new JObject { ["designation"] = "-WReview103", ["cores"] = new JArray(selected.DeepClone()) });
        Assert.Equal("cable_core_segment", (string?)Assert.Single(Review("cable_cores", expected))["code"]);
        branch["definitionPoints"] = new JArray();
        Assert.Empty(Review("cable_cores", expected));
    }
    [Theory]
    [InlineData("error")]
    [InlineData("warning")]
    [InlineData("info")]
    public void StoredMessages_PreserveSeverityAndNeverRunCheck(string severity)
    {
        Assert.Empty(Review("eplan_messages"));
        _actual["messages"] = new JArray(new JObject { ["severity"] = severity, ["text"] = "Stored message", ["objects"] = new JArray() });
        JToken issue = Assert.Single(Review("eplan_messages"));
        Assert.Equal(severity, (string?)issue["severity"]);
        Assert.Equal("eplan_check_list", (string?)issue["fix"]?["tool"]);
    }
}

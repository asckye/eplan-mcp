using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class PageReviewRulesTests
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

    /// <summary>Corpus 2026-11-17 (A147): property texts read longer than their ink; macro box frames and loose lines are no symbols.</summary>
    [Theory]
    [InlineData("PropertyPlacement", "-K1")]
    [InlineData("MacroBox", null)]
    [InlineData("Line", null)]
    public void OutlinesOtherThanSymbols_LeavingTheArea_OnlyWarn(string kind, string? name)
    {
        _scene.Page.PageSize = [420, 297];
        _scene.Page.DrawingArea = [12, 16, 412, 292];
        DrawingPlacement p = Placement(1, 60, kind, name);
        p.Location = [60, 236];
        p.Bounds = [-5.55, 254.76, 39, 260.92];
        Assert.Equal("warning", (string?)Assert.Single(Review("off_area"))["severity"]);
    }

    /// <summary>Corpus 2026-11-17 (official PLC sample =K3+B2&amp;EFS/1): the card picture is a graphic block inside the PLC box.</summary>
    [Fact]
    public void AGraphicBlockInsideABox_OnlyWarns()
    {
        Placement(1, 52, "PLC", "-KE1").Bounds = [52, 188, 72, 260];
        Placement(2, 52, "Block", "144_1428091").Bounds = [52.0007, 191.2, 72.14, 256.3];
        Assert.Equal("warning", (string?)Assert.Single(Review("overlaps"))["severity"]);
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

    /// <summary>Test audit 2026-11-17: cross_reference_clash had a Large test only.</summary>
    [Theory]
    [InlineData(100, true)]   // on the cross-reference text
    [InlineData(140, false)]  // clear of it
    public void ASymbolOnACrossReferenceText_IsAWarningWithAMoveFix(double x, bool reported)
    {
        DrawingPlacement reference = Placement(1, 100, "Text");
        reference.Properties["cross_reference"] = "true";
        Placement(2, x, "Function", "-K1");
        JArray issues = Review("cross_reference_clash");
        if (!reported)
        {
            Assert.Empty(issues);
            return;
        }
        JToken issue = Assert.Single(issues);
        Assert.Equal(("cross_reference_clash", "warning", "eplan_placement_update"), ((string?)issue["code"], (string?)issue["severity"], (string?)issue["fix"]!["tool"]));
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

    /// <summary>Live 2026-11-16: model view pages project the 3D layout; corpus 2026-11-17: P&amp;ID pages draw pipes.</summary>
    [Theory]
    [InlineData("ModelView")]
    [InlineData("ProcessAndInstrumentationDiagram")]
    public void NonSchematicPages_SkipTheSchematicLayoutRules(string type)
    {
        _scene.PageType = type;
        Placement(1, 8, "Function", "-K1").Bounds = [4, 4, 12, 12];
        Placement(2, 9, "Function", "-K2").Bounds = [8, 8, 16, 16];
        _scene.Graphical.Add([[0, 10], [20, 10]]);
        Assert.Equal("warning", (string?)Assert.Single(Review("overlaps"))["severity"]);
        Assert.Empty(Review("wire_through_symbol"));
    }
}

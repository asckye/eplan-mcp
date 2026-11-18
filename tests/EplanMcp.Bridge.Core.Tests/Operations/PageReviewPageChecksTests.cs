using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class PageReviewRulesTests
{
    private DrawingPlacement ColumnDevice(double x = -4, string kind = "Function", string? name = "-K1")
    {
        _scene.Page.ColumnEdges = [-8, 0, 8];
        return Placement(1, x, kind, name);
    }

    private void AssertColumnWarning(DrawingPlacement p, string columns, int home)
    {
        JToken issue = Assert.Single(Review("path_straddle"));
        Assert.Equal(("path_straddle", "warning"), ((string?)issue["code"], (string?)issue["severity"]));
        JToken locator = Assert.Single(Assert.IsType<JArray>(issue["objects"]));
        Assert.Equal((p.Locator.Type, p.Locator.Oid), ((int)locator["type"]!, (long)locator["oid"]!));
        Assert.Equal(p.Location, issue["position"]!.ToObject<double[]>());
        Assert.Equal(FormattableString.Invariant($"Pins lie in columns {columns}, the symbol is numbered in column {home}; keep the symbol and its pins in one column."), (string?)issue["message"]);
    }

    [Theory]
    [InlineData(-1.0000000000000002e-6, false)]
    [InlineData(-1e-6, true)]
    [InlineData(-0.9999999999999997e-6, true)]
    public void PathStraddle_PinAtColumnTolerance_UsesNextColumnAtEquality(double x, bool reported)
    {
        var p = ColumnDevice();
        _scene.Pins["13/1/0"] = new() { Location = [-4, 8] };
        _scene.Pins["13/1/1"] = new() { Location = [x, 8] };
        _scene.Pins["13/1/2"] = new() { Location = [x, 9] };
        if (reported)
        {
            AssertColumnWarning(p, "1, 2", 1);
            return;
        }
        Assert.Empty(Review("path_straddle"));
    }

    [Theory]
    [InlineData(-1.0000000000000002e-6, true)]
    [InlineData(-1e-6, false)]
    [InlineData(-0.9999999999999997e-6, false)]
    public void PathStraddle_InsertionAtColumnTolerance_UsesNextColumnAtEquality(double x, bool reported)
    {
        var p = ColumnDevice(x);
        _scene.Pins["13/1/0"] = new() { Location = [4, 8] };
        if (reported)
        {
            AssertColumnWarning(p, "2", 1);
            return;
        }
        Assert.Empty(Review("path_straddle"));
    }

    [Theory]
    [InlineData(-1.0000000000000002e-6, false, false)]
    [InlineData(-1e-6, false, true)]
    [InlineData(-0.9999999999999997e-6, false, true)]
    [InlineData(-1.0000000000000002e-6, true, true)]
    [InlineData(-1e-6, true, false)]
    [InlineData(-0.9999999999999997e-6, true, false)]
    public void PathStraddle_HomeAtOuterEdge_RequiresValidColumn(double home, bool lastEdge, bool reported)
    {
        var p = ColumnDevice(home);
        _scene.Page.ColumnEdges = lastEdge ? [-16, -8, 0] : [0, 8, 16];
        _scene.Pins["13/1/0"] = new() { Location = [lastEdge ? -12 : 12, 8] };
        if (reported)
        {
            AssertColumnWarning(p, lastEdge ? "1" : "2", lastEdge ? 2 : 1);
            return;
        }
        Assert.Empty(Review("path_straddle"));
    }

    [Theory]
    [InlineData(-9, "0")]
    [InlineData(8, "3")]
    public void PathStraddle_PinOutsideKnownColumns_ReportsOutOfRangeColumn(double x, string column)
    {
        var p = ColumnDevice();
        _scene.Pins["13/1/0"] = new() { Location = [x, 8] };
        AssertColumnWarning(p, column, 1);
    }

    [Fact]
    public void PathStraddle_PinWithUnreadablePosition_IsNotAColumn()
    {
        ColumnDevice();
        _scene.Pins["13/1/0"] = new() { Location = [double.NaN, 8] };
        Assert.Empty(Review("path_straddle"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new double[] { -8, 0 })]
    [InlineData(new double[] { -8, 0, 8 })]
    [InlineData(new double[] { -8, 0, 8, 16 })]
    public void PathStraddle_ColumnEdgeCount_RequiresAtLeastThree(double[]? edges)
    {
        var p = ColumnDevice();
        _scene.Page.ColumnEdges = edges;
        _scene.Pins["13/1/0"] = new() { Location = [4, 8] };
        if (edges?.Length >= 3)
        {
            AssertColumnWarning(p, "2", 1);
            return;
        }
        Assert.Empty(Review("path_straddle"));
    }

    [Theory]
    [InlineData("Function", null, false, false)]
    [InlineData("Function", "-K1", true, false)]
    [InlineData("LocationBox", "-K1", false, false)]
    [InlineData("PLC", "-K1", false, false)]
    [InlineData("PLCBox", "-K1", false, false)]
    [InlineData("Box", "-K1", false, false)]
    [InlineData("Function", "-K1", false, true)]
    public void PathStraddle_UnnamedChildOrContainer_IsExempt(string kind, string? name, bool child, bool container)
    {
        var p = ColumnDevice(kind: kind, name: name);
        p.Parent = child ? new() { Oid = 2 } : null;
        p.Properties["layout_container"] = container ? "true" : "false";
        _scene.Pins["13/1/0"] = new() { Location = [4, 8] };
        Assert.Empty(Review("path_straddle"));
    }

    [Fact]
    public void PathStraddle_MissingInsertionPoint_SkipsDevice()
    {
        ColumnDevice().Location = [];
        _scene.Pins["13/1/0"] = new() { Location = [4, 8] };
        Assert.Empty(Review("path_straddle"));
    }

    [Fact]
    public void PathStraddle_EmptyNameWithKnownInsertionAndPin_ReportsDevice()
    {
        var p = ColumnDevice(name: "");
        p.Bounds = null;
        _scene.Pins["13/1/0"] = new() { Location = [4, 8] };
        AssertColumnWarning(p, "2", 1);
    }

    [Fact]
    public void PathStraddle_NoReadableOwnedPins_SkipsDevice()
    {
        ColumnDevice();
        _scene.Pins["13/1/0"] = new() { Location = [] };
        _scene.Pins["13/10/0"] = new() { Location = [4, 8] };
        _scene.Pins["17/1/0"] = new() { Location = [4, 8] };
        Assert.Empty(Review("path_straddle"));
    }

    [Fact]
    public void PathStraddle_CheckDisabled_SkipsDevice()
    {
        ColumnDevice();
        _scene.Pins["13/1/0"] = new() { Location = [4, 8] };
        Assert.Empty(Review("page_setup"));
    }

    private JObject Setup(params JObject[] peers)
    {
        var setup = new JObject { ["type"] = "Circuit", ["frame"] = "Mine", ["grid"] = 4, ["peers"] = new JArray(peers) };
        _actual["pageSetup"] = setup;
        return setup;
    }

    private static JObject Frame(string frame) => new() { ["frame"] = frame };

    private static void AssertSetupWarning(JToken issue, string argument, JToken wanted)
    {
        Assert.Equal(("page_setup", "warning", Page), ((string?)issue["code"], (string?)issue["severity"], (string?)issue["page"]));
        Assert.Empty(Assert.IsType<JArray>(issue["objects"]));
        JToken update = Assert.Single(Assert.IsType<JArray>(issue["fix"]!["args"]!["pages"]));
        Assert.Equal(Page, (string?)update["name"]);
        Assert.True(JToken.DeepEquals(wanted, update[argument]));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void PageSetup_PeerCount_RequiresAtLeastThree(int count, bool reported)
    {
        Setup([.. Enumerable.Range(0, count).Select(_ => Frame("Other"))]);
        if (reported)
        {
            AssertSetupWarning(Assert.Single(Review("page_setup")), "plot_frame", "Other");
            return;
        }
        Assert.Empty(Review("page_setup"));
    }

    [Theory]
    [InlineData("Mine", "Mine", "Other")]
    [InlineData("A", "B", "C")]
    [InlineData(null, null, "Other")]
    public void PageSetup_MatchingTiedOrUnknownWinner_SkipsField(string? first, string? second, string? third)
    {
        Setup([.. new[] { first, second, third }.Select(f => f is null ? new JObject() : Frame(f))]);
        Assert.Empty(Review("page_setup"));
    }

    [Fact]
    public void PageSetup_MostCommonValueBelowHalf_SkipsField()
    {
        Setup(Frame("Other"), Frame("Other"), Frame("Mine"), Frame("B"), Frame("C"));
        Assert.Empty(Review("page_setup"));
    }

    [Theory]
    [InlineData(2, 3, true)]
    [InlineData(2, 2, false)]
    [InlineData(3, 2, false)]
    public void PageSetup_CurrentValueAroundHalf_SkipsTieAndMatchingWinner(int mine, int other, bool reported)
    {
        Setup([.. Enumerable.Repeat("Mine", mine).Concat(Enumerable.Repeat("Other", other)).Select(Frame)]);
        if (reported)
        {
            AssertSetupWarning(Assert.Single(Review("page_setup")), "plot_frame", "Other");
            return;
        }
        Assert.Empty(Review("page_setup"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageSetup_UnknownCurrentFields_AreNotADifference(bool explicitNull)
    {
        var peer = new JObject { ["frame"] = "Other", ["grid"] = 8 };
        JObject setup = Setup(peer, (JObject)peer.DeepClone(), new());
        setup.Remove("frame");
        setup.Remove("grid");
        if (explicitNull)
        {
            setup["frame"] = JValue.CreateNull();
            setup["grid"] = JValue.CreateNull();
        }
        Assert.Empty(Review("page_setup"));
    }

    [Fact]
    public void PageSetup_OnlyGridDiffers_NamesPageAndPeerGrid()
    {
        Setup([.. Enumerable.Range(0, 3).Select(_ => new JObject { ["frame"] = "Mine", ["grid"] = 8 })]);
        AssertSetupWarning(Assert.Single(Review("page_setup")), "grid_size_mm", 8);
    }

    [Fact]
    public void PageSetup_EqualGridWithDifferentJsonNumberTypes_IsOneGrid()
    {
        Setup([.. Enumerable.Range(0, 3).Select(_ => new JObject { ["grid"] = 4.0 })]);
        Assert.Empty(Review("page_setup"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"peers\":{}}")]
    public void PageSetup_SetupOrPeerArrayUnavailable_SkipsPage(string? json)
    {
        if (json is not null)
        {
            _actual["pageSetup"] = JToken.Parse(json);
        }
        Assert.Empty(Review("page_setup"));
    }

    [Fact]
    public void PageSetup_CheckDisabled_SkipsOddFrame()
    {
        Setup(Frame("Other"), Frame("Other"), Frame("Other"));
        Assert.Empty(Review("path_straddle"));
    }

    private (DrawingPlacement Text, DrawingPlacement Box) Crossing(string textKind = "Text", string boxKind = "LocationBox")
    {
        var box = Placement(1, kind: boxKind);
        box.Bounds = [0, 0, 10, 10];
        var text = Placement(2, kind: textKind);
        text.Bounds = [-4, 2, 4, 6];
        return (text, box);
    }

    private void AssertCrossing(DrawingPlacement text, DrawingPlacement box, bool reported)
    {
        JArray issues = Review("box_crossings");
        if (!reported)
        {
            Assert.Empty(issues);
            return;
        }
        JToken issue = Assert.Single(issues);
        Assert.Equal(("text_crosses_box", "warning"), ((string?)issue["code"], (string?)issue["severity"]));
        Assert.Equal(new[] { (text.Locator.Type, text.Locator.Oid), (box.Locator.Type, box.Locator.Oid) },
            issue["objects"]!.Select(o => ((int)o["type"]!, (long)o["oid"]!)));
        Assert.Equal(text.Location, issue["position"]!.ToObject<double[]>());
    }

    // Reflect the left border through zero, then swap axes to exercise all four comparisons at the same precision.
    private static double[] BorderBounds(double[] bounds, int side)
    {
        double[] b = side % 2 == 0 ? bounds : [-bounds[2], bounds[1], -bounds[0], bounds[3]];
        return side < 2 ? b : [b[1], b[0], b[3], b[2]];
    }

    [Theory]
    [InlineData(0.9999999999999997e-6, false)]
    [InlineData(1e-6, false)]
    [InlineData(1.0000000000000002e-6, true)]
    public void TextCrossesBox_OutsideTextReachingBorder_RequiresPenetrationBeyondTolerance(double depth, bool reported)
    {
        var (text, box) = Crossing();
        for (int side = 0; side < 4; side++)
        {
            box.Bounds = BorderBounds([0, 0, 10, 10], side);
            text.Bounds = BorderBounds([-4, 2, depth, 6], side);
            AssertCrossing(text, box, reported);
        }
    }

    [Theory]
    [InlineData(0.9999999999999997e-6, false)]
    [InlineData(1e-6, false)]
    [InlineData(1.0000000000000002e-6, true)]
    public void TextCrossesBox_InsideTextLeavingBorder_RequiresOverhangBeyondTolerance(double excess, bool reported)
    {
        var (text, box) = Crossing("PropertyPlacement");
        for (int side = 0; side < 4; side++)
        {
            box.Bounds = BorderBounds([0, 0, 10, 10], side);
            text.Bounds = BorderBounds([-excess, 2, 4, 6], side);
            AssertCrossing(text, box, reported);
        }
    }

    [Theory]
    [InlineData("Text", "LocationBox", true)]
    [InlineData("PropertyPlacement", "LocationBox", true)]
    [InlineData("PathText", "LocationBox", false)]
    [InlineData("Text", "Box", false)]
    public void TextCrossesBox_PlacementKinds_SelectOnlyTextAndLocationBox(string textKind, string boxKind, bool reported)
    {
        var (text, box) = Crossing(textKind, boxKind);
        AssertCrossing(text, box, reported);
    }

    [Theory]
    [InlineData(13, 1, false)]
    [InlineData(17, 1, true)]
    [InlineData(13, 2, true)]
    public void TextCrossesBox_ParentLocator_ExemptsOnlyMatchingTypeAndOid(int type, long oid, bool reported)
    {
        var (text, box) = Crossing("PropertyPlacement");
        text.Parent = new() { Type = type, Oid = oid };
        AssertCrossing(text, box, reported);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new double[] { 1, 2, 3 })]
    [InlineData(new double[] { 3, 2, 2, 6 })]
    [InlineData(new double[] { -4, 2, 4, 2 })]
    public void TextCrossesBox_MissingOrInvalidBounds_SkipsEitherPlacement(double[]? bounds)
    {
        var (text, box) = Crossing();
        double[]? original = text.Bounds;
        text.Bounds = bounds;
        AssertCrossing(text, box, false);
        text.Bounds = original;
        box.Bounds = bounds;
        AssertCrossing(text, box, false);
    }

    [Theory]
    [InlineData(-double.Epsilon, false)]
    [InlineData(0, false)]
    [InlineData(double.Epsilon, true)]
    public void TextCrossesBox_ZeroArea_RequiresPositiveExtent(double extent, bool reported)
    {
        var (text, box) = Crossing();
        text.Bounds = [0, -4, extent, 4];
        box.Bounds = [-5, 0, 5, 10];
        AssertCrossing(text, box, reported);
        text.Bounds = [2, -4, 6, 4];
        box.Bounds = [0, 0, 10, extent];
        AssertCrossing(text, box, reported);
    }

    [Fact]
    public void TextCrossesBox_CheckDisabled_SkipsCrossing()
    {
        Crossing();
        Assert.Empty(Review("path_straddle"));
    }
}

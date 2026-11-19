using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class PageReviewRulesTests
{
    private sealed class ThresholdReader(PageScene scene, JObject actual) : PageReviewOperation.IReader
    {
        public (PageScene Scene, JObject Actual) Read(string project, string page, JObject request) => (scene, actual);
    }

    private JObject ReviewLayout(string check, JObject? layout = null)
    {
        var operation = new PageReviewOperation(new ThresholdReader(_scene, _actual));
        var args = new JObject { ["project"] = "Line1", ["page"] = Page, ["checks"] = new JArray(check) };
        if (layout is not null)
        { args["layout"] = layout; }
        return Assert.IsType<JObject>(operation.Execute((object)args));
    }

    [Theory]
    [InlineData(5, 6, false)]
    [InlineData(3, 2, true)]
    public void RequestSpacing_ChangesOnlyTheParallelNetWarning(double gap, double spacing, bool defaultWarns)
    {
        ParallelWires(gap);
        _scene.ConfigLayoutMembers.Add("MinimumTextHeightMm");
        Assert.Equal(defaultWarns, ReviewLayout("net_overlap")["issues"]!.HasValues);
        JObject result = ReviewLayout("net_overlap", new() { ["parallelSpacingMm"] = spacing });
        if (defaultWarns)
        { Assert.Empty(result["issues"]!); }
        else
        {
            JToken issue = Assert.Single(result["issues"]!);
            Assert.Equal(("net_overlap", "warning"), ((string?)issue["code"], (string?)issue["severity"]));
            Assert.StartsWith("R6:", (string?)issue["message"], StringComparison.Ordinal);
        }
        JToken layout = result["layout"]!;
        Assert.Equal((spacing, 0.5, 10), ((double)layout["parallelSpacingMm"]!, (double)layout["minimumTextHeightMm"]!, (double)layout["marginMm"]!));
        Assert.Equal(("request", "config", "default"), ((string?)layout["sources"]!["parallelSpacingMm"], (string?)layout["sources"]!["minimumTextHeightMm"], (string?)layout["sources"]!["marginMm"]));
        Assert.Equal(defaultWarns, ReviewLayout("net_overlap")["issues"]!.HasValues);
    }

    [Theory]
    [InlineData("1", 2, false)]
    [InlineData("0.4", 0.25, true)]
    public void RequestTextHeight_ChangesOnlyTheTextFormatWarning(string height, double minimum, bool defaultWarns)
    {
        Placement(1, kind: "Text").Properties["height_mm"] = height;
        _scene.Layout.MarginMm = 7;
        _scene.ConfigLayoutMembers.Add("marginMm");
        Assert.Equal(defaultWarns, ReviewLayout("text_format")["issues"]!.HasValues);
        JObject result = ReviewLayout("text_format", new() { ["minimumTextHeightMm"] = minimum });
        if (defaultWarns)
        { Assert.Empty(result["issues"]!); }
        else
        {
            JToken issue = Assert.Single(result["issues"]!);
            Assert.Equal(("text_format", "warning"), ((string?)issue["code"], (string?)issue["severity"]));
            Assert.StartsWith("R8:", (string?)issue["message"], StringComparison.Ordinal);
        }
        JToken layout = result["layout"]!;
        Assert.Equal(JTokenType.Null, layout["parallelSpacingMm"]!.Type);
        Assert.Equal((minimum, 7), ((double)layout["minimumTextHeightMm"]!, (double)layout["marginMm"]!));
        Assert.Equal("config", (string?)layout["sources"]!["marginMm"]);
        Assert.Equal("request", (string?)layout["sources"]!["minimumTextHeightMm"]);
    }

    [Fact]
    public void RequestMargin_ConstrainsTheOverlapRepairOnAnUnframedPage()
    {
        _scene.Page.PageSize = [80, 80];
        foreach (int oid in new[] { 1, 2 })
        {
            DrawingPlacement p = Placement(oid, kind: "Function", name: "-K" + oid);
            p.Location = [20, 20];
            p.Bounds = [18, 18, 22, 22];
        }
        JToken Move(JObject result) => Assert.Single(result["issues"]!)["fix"]!["args"]!["moves"]![0]!;
        JToken before = Move(ReviewLayout("overlaps"));
        Assert.True((double)before["x_mm"]! < 22 || (double)before["y_mm"]! < 22);
        JObject after = ReviewLayout("overlaps", new() { ["marginMm"] = 20 });
        JToken move = Move(after);
        Assert.InRange((double)move["x_mm"]!, 22, 58);
        Assert.InRange((double)move["y_mm"]!, 22, 58);
        Assert.Equal("request", (string?)after["layout"]!["sources"]!["marginMm"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PageExhaustingMargin_UsesFrameOrFallsBackPerMember(bool frame, bool configured)
    {
        _scene.Page.PageSize = [100, 80];
        if (frame)
        { _scene.Page.DrawingArea = [1, 1, 99, 79]; }
        if (configured)
        {
            _scene.ConfigLayoutMembers.Add("marginMm");
            _scene.Layout.MarginMm = 8;
        }
        JObject result = ReviewLayout("net_overlap", new() { ["marginMm"] = 40, ["parallelSpacingMm"] = 0 });
        JToken layout = result["layout"]!;
        Assert.Equal(frame ? 40 : configured ? 8 : 10, (double)layout["marginMm"]!);
        Assert.Equal(frame ? "request" : configured ? "config" : "default", (string?)layout["sources"]!["marginMm"]);
        Assert.Equal(0, (double)layout["parallelSpacingMm"]!);
        if (frame)
        { Assert.Null(result["layoutWarnings"]); }
        else
        { Assert.Contains("review.layout.margin_mm", (string?)result["layoutWarnings"]!["marginMm"], StringComparison.Ordinal); }
    }
}

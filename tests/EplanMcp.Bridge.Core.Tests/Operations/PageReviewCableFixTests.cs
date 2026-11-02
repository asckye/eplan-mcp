using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageReviewCableFixTests
{
    private readonly ConnectionCreatePlanner.Scene _scene = TNodeFixture.Scene();
    private readonly DrawingLocator _cable = new() { Project = "LargeWrite", Page = "=GAA+A1&EFS1/103", Type = 17, Oid = 69845, Digest = new string('a', 64) };

    private (JObject Actual, JObject Request) Evidence()
    {
        _scene.Project.Name = "LargeWrite";
        JObject Connection(long oid, string from, string to, double[] start, double[] end) => EplanPageReviewReader.ConnectionRow(new()
        {
            Locator = new() { Type = 26, Oid = oid },
            Points = [start, end],
            Path = CableDefinitionPlanner.DrawnPath(_scene, start, end),
            Label = "1",
            Colour = "RD"
        }, new JObject { ["device"] = from, ["pin"] = "1" }, new JObject { ["device"] = to, ["pin"] = "1" }, [42]);
        JObject target = Connection(72612, "=GAA+A1-R103A", "=GAA+A1-R103B", [120, 220], [120, 100]);
        JObject other = Connection(72613, "=GAA+A1-R103B", "=GAA+A1-R103C", [120, 100], [200, 160]);
        var connections = new JArray(target, other);
        JObject cable = EplanPageReviewReader.CableRow("+A1-WReview103", _cable, [72612, 72613], connections, [80, 128], [[80, 128], [160, 128]]);
        return (new() { ["connections"] = connections, ["cables"] = new JArray(cable) }, new()
        {
            ["checks"] = new JArray("cable_cores"),
            ["expected"] = new JObject { ["cables"] = new JArray(new JObject { ["designation"] = "+A1-WReview103", ["cores"] = new JArray(target.DeepClone()) }) }
        });
    }

    [Fact]
    public void LiveTNodePins_CableMovesFromTrunk128ToExclusiveBranch192()
    {
        var (actual, request) = Evidence();
        Assert.Equal(new double[][] { [120, 220], [120, 160], [120, 100] }, actual["connections"]![0]!["drawnPath"]!.ToObject<double[][]>());
        Assert.Equal(new double[][] { [120, 100], [120, 160], [122, 160], [200, 160] }, actual["connections"]![1]!["drawnPath"]!.ToObject<double[][]>());
        JToken issue = Assert.Single(PageReviewRules.Check(_scene, actual, request));
        Assert.Equal("cable_core_segment", (string?)issue["code"]);
        // A73: a move drops the core assignment, so the computed position is advice and the fix is a redraw.
        Assert.Equal("eplan_connection_create", (string?)issue["fix"]!["tool"]);
        JToken args = issue["fix"]!["args"]!;
        Assert.Equal("LargeWrite", (string?)args["project"]);
        Assert.Equal(_scene.Page.Page, (string?)args["page"]);
        Assert.Null(args["moves"]);
        Assert.Equal([80d, 192d], issue["position"]!.Values<double>());
        Assert.Contains("(80, 192)", (string?)issue["message"], StringComparison.Ordinal);
        Assert.Contains("drops its core assignment", (string?)issue["message"], StringComparison.Ordinal);
        JToken move = Assert.Single(PageReviewCableFix.Arguments(_scene.Page.GridSize, actual["cables"]![0], actual["connections"]![0], (JArray)actual["connections"]!)["moves"]!);
        Assert.True(JToken.DeepEquals(BridgeJson.FromObject(_cable), move["locator"]));
        Assert.Null(move["delta_mm"]);
        Assert.InRange(192d, 160 + 4, 220 - 4);
    }

    [Fact]
    public void NonParallelExclusiveBranch_RequiresRedrawingWithoutCoordinates()
    {
        var (actual, request) = Evidence();
        actual["connections"]![0]!["drawnPath"] = JArray.FromObject(new double[][] { [200, 160], [120, 160], [120, 100] });
        AssertRedraw(actual, request);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("other_path")]
    [InlineData("points")]
    [InlineData("position")]
    [InlineData("locator")]
    [InlineData("connection")]
    [InlineData("grid")]
    [InlineData("invalid_point")]
    public void MissingOrUnreadableEvidence_RequiresRedrawingWithoutCoordinates(string missing)
    {
        var (actual, request) = Evidence();
        JToken cable = actual["cables"]![0]!;
        switch (missing)
        {
            case "path":
                actual["connections"]![0]!["drawnPath"] = null;
                break;
            case "other_path":
                actual["connections"]![1]!["drawnPath"] = null;
                break;
            case "connection":
                ((JArray)actual["connections"]!).RemoveAt(0);
                break;
            case "grid":
                _scene.Page.GridSize = 0;
                break;
            case "invalid_point":
                cable["points"] = new JArray(new JArray("unknown", 128), new JArray(160, 128));
                break;
            default:
                cable[missing] = null;
                break;
        }
        AssertRedraw(actual, request);
    }

    [Fact]
    public void CableLineThatDoesNotCrossSharedTrunk_RequiresRedrawingWithoutCoordinates()
    {
        var (actual, request) = Evidence();
        actual["cables"]![0]!["points"] = JArray.FromObject(new double[][] { [140, 128], [160, 128] });
        AssertRedraw(actual, request);
    }

    [Theory]
    [InlineData(160, 166)]
    [InlineData(160, 168)]
    public void BranchMustHaveGridPointWithOneGridClearanceAtBothEnds(double low, double high)
    {
        var (actual, request) = Evidence();
        actual["connections"]![0]!["drawnPath"] = JArray.FromObject(new double[][] { [120, high], [120, low], [120, 100] });
        if (high - low < 8)
        { AssertRedraw(actual, request); }
        else
        {
            JToken issue = Assert.Single(PageReviewRules.Check(_scene, actual, request));
            Assert.Equal(164, (double)issue["position"]![1]!);
        }
    }

    [Fact]
    public void VerticalCableAndHorizontalParallelBranch_MoveOnlyXFromObservedLocation()
    {
        var (actual, request) = Evidence();
        foreach (JToken c in actual["connections"]!)
        { c["drawnPath"] = JArray.FromObject(c["drawnPath"]!.ToObject<double[][]>()!.Select(p => new[] { p[1], p[0] })); }
        actual["cables"]![0]!["position"] = new JArray(132, 84);
        actual["cables"]![0]!["points"] = JArray.FromObject(new double[][] { [128, 80], [128, 160] });
        JToken issue = Assert.Single(PageReviewRules.Check(_scene, actual, request));
        Assert.Equal([196d, 84d], issue["position"]!.Values<double>());
    }

    [Fact]
    public void OffGridInsertionOffset_DoesNotResnapTheAlreadySnappedLine()
    {
        var (actual, request) = Evidence();
        actual["cables"]![0]!["position"] = new JArray(81, 130);
        Assert.Equal([81d, 194d], Assert.Single(PageReviewRules.Check(_scene, actual, request))["position"]!.Values<double>());
        JObject args = PageReviewCableFix.Arguments(_scene.Page.GridSize, actual["cables"]![0], actual["connections"]![0], (JArray)actual["connections"]!);
        Assert.False((bool)args["snap_to_grid"]!);
        Assert.Equal(81, (double)args["moves"]![0]!["x_mm"]!);
        Assert.Equal(194, (double)args["moves"]![0]!["y_mm"]!);
    }

    private void AssertRedraw(JObject actual, JObject request)
    {
        JToken issue = Assert.Single(PageReviewRules.Check(_scene, actual, request));
        Assert.Equal("eplan_connection_create", (string?)issue["fix"]!["tool"]);
        Assert.Contains("redraw the cable across the intended branch", (string)issue["message"]!, StringComparison.Ordinal);
        Assert.Null(issue["fix"]!["args"]!["moves"]);
        Assert.DoesNotContain("delta_mm", issue["fix"]!.ToString(), StringComparison.Ordinal);
        Assert.Equal("LargeWrite", (string?)issue["fix"]!["args"]!["project"]);
    }
}

using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class GraphicEditGeometryTests
{
    public static IEnumerable<object[]> Corners()
    {
        foreach (string operation in new[] { "chamfer", "fillet" })
        {
            foreach (int x in new[] { -1, 1 })
            {
                foreach (int y in new[] { -1, 1 })
                {
                    foreach (bool reverseFirst in new[] { false, true })
                    {
                        foreach (bool reverseSecond in new[] { false, true })
                        { yield return [operation, x, y, reverseFirst, reverseSecond]; }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Corners))]
    public void Corner_AllQuadrantsAndEndpointCombinations_TrimAndConnect(string operation, int x, int y, bool reverseFirst, bool reverseSecond)
    {
        double[] vertex = [30, 30], firstFar = [30 + x * 20, 30], secondFar = [30, 30 + y * 20];
        JObject first = Line(reverseFirst ? firstFar : vertex, reverseFirst ? vertex : firstFar);
        JObject second = Line(reverseSecond ? secondFar : vertex, reverseSecond ? vertex : secondFar);
        first["pen"] = new JObject { ["color"] = 5, ["width"] = 0.35 };
        first["layer"] = 10;
        var selected = new JArray(first, second);
        JArray before = (JArray)selected.DeepClone();
        JObject result = GraphicEditGeometry.Plan(new JObject { ["operation"] = operation, [operation == "fillet" ? "radius_mm" : "distance_mm"] = 5 }, selected);
        double[] firstTangent = [30 + x * 5, 30], secondTangent = [30, 30 + y * 5];
        Point(firstTangent, result["predicted"]![0]!["points"]![reverseFirst ? 1 : 0]!);
        Point(firstFar, result["predicted"]![0]!["points"]![reverseFirst ? 0 : 1]!);
        Point(secondTangent, result["predicted"]![1]!["points"]![reverseSecond ? 1 : 0]!);
        Point(secondFar, result["predicted"]![1]!["points"]![reverseSecond ? 0 : 1]!);
        JToken created = Assert.Single(result["created"]!);
        Assert.True(JToken.DeepEquals(first["pen"], created["pen"]));
        Assert.Equal(10, (int)created["layer"]!);
        if (operation == "chamfer")
        {
            Assert.Equal("line", (string?)created["kind"]);
            Point(firstTangent, created["position"]!);
            Point(secondTangent, created["endPosition"]!);
        }
        else
        {
            Point([30 + x * 5, 30 + y * 5], created["position"]!);
            Assert.Equal("arc", (string?)created["kind"]);
            Assert.Equal(5, (double)created["radiusMm"]!);
            double start = (double)created["startAngle"]!, end = (double)created["endAngle"]!;
            Assert.Equal(90, (end - start + 360) % 360, 7);
            double[][] endpoints = [ArcPoint(created, start), ArcPoint(created, end)];
            Assert.Contains(endpoints, p => Distance(p, firstTangent) < 1e-8);
            Assert.Contains(endpoints, p => Distance(p, secondTangent) < 1e-8);
        }
        Assert.True(JToken.DeepEquals(before, selected));
    }

    [Fact]
    public void Fillet_AcuteAngle_UsesTangentDistanceAndSmallArc()
    {
        var selected = new JArray(Line([0, 0], [20, 0]), Line([0, 0], [10, 10 * Math.Sqrt(3)]));
        JObject plan = GraphicEditGeometry.Plan(new JObject { ["operation"] = "fillet", ["radius_mm"] = 5 }, selected);
        Point([5 * Math.Sqrt(3), 0], plan["predicted"]![0]!["points"]![0]!);
        Point([5 * Math.Sqrt(3), 5], plan["created"]![0]!["position"]!);
        Assert.Equal(120, ((double)plan["created"]![0]!["endAngle"]! - (double)plan["created"]![0]!["startAngle"]! + 360) % 360, 7);
    }

    [Theory]
    [InlineData("chamfer", 0)]
    [InlineData("chamfer", -1)]
    [InlineData("chamfer", 21)]
    [InlineData("fillet", 0)]
    [InlineData("fillet", -1)]
    [InlineData("fillet", 21)]
    [InlineData("fillet", double.NaN)]
    [InlineData("chamfer", double.PositiveInfinity)]
    public void Corner_InvalidSize_Rejects(string operation, double size) => Invalid(() => GraphicEditGeometry.Plan(new JObject { ["operation"] = operation, [operation == "fillet" ? "radius_mm" : "distance_mm"] = size }, new JArray(Line([0, 0], [20, 0]), Line([0, 0], [0, 20]))));

    [Theory]
    [InlineData(0, 0, -20, 0)]
    [InlineData(0, 0, 20, 0)]
    [InlineData(1, 1, 1, 20)]
    [InlineData(20, 0, 0, 0)]
    public void Corner_ParallelDisjointOrDuplicateEndpoints_Rejects(double x1, double y1, double x2, double y2) => Invalid(() => GraphicEditGeometry.Plan(new JObject { ["operation"] = "chamfer", ["distance_mm"] = 2 }, new JArray(Line([0, 0], [20, 0]), Line([x1, y1], [x2, y2]))));

    [Fact]
    public void Fillet_TangentLengthExceedsAvailable_Rejects()
    {
        Invalid(() => GraphicEditGeometry.Plan(new JObject { ["operation"] = "fillet", ["radius_mm"] = 6 }, new JArray(Line([0, 0], [10, 0]), Line([0, 0], [5, 5 * Math.Sqrt(3)]))));
    }

    [Theory]
    [InlineData("start", 10, 10, 16, 18)]
    [InlineData("end", 7, 6, 13, 14)]
    public void SetLength_PreservesAnchorAndDirection(string anchor, double x1, double y1, double x2, double y2)
    {
        JObject result = GraphicEditGeometry.Plan(new JObject { ["operation"] = "set_length", ["length_mm"] = 10, ["anchor"] = anchor }, new JArray(Line([10, 10], [13, 14])));
        Point([x1, y1], result["predicted"]![0]!["points"]![0]!);
        Point([x2, y2], result["predicted"]![0]!["points"]![1]!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SetLength_InvalidLength_Rejects(double length) => Invalid(() => GraphicEditGeometry.Plan(new JObject { ["operation"] = "set_length", ["length_mm"] = length }, new JArray(Line([10, 10], [13, 14]))));

    [Fact]
    public void SetLength_UnknownAnchorAndZeroSource_Reject()
    {
        Invalid(() => GraphicEditGeometry.Plan(new JObject { ["operation"] = "set_length", ["length_mm"] = 10, ["anchor"] = "middle" }, new JArray(Line([10, 10], [13, 14]))));
        Invalid(() => GraphicEditGeometry.Plan(new JObject { ["operation"] = "set_length", ["length_mm"] = 10 }, new JArray(Line([10, 10], [10, 10]))));
    }

    [Fact]
    public void Stretch_LowerRectangleEdge_UsesOnlyPerpendicularOffset()
    {
        JObject result = Stretch(Rectangle(), [9, 9, 21, 11], [5, 3]);
        Point([10, 13, 20, 20], result["predicted"]![0]!["bounds"]!);
        Assert.Contains("ignored", (string)result["warning"]!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9, 9, 11, 11, 15, 13, 20, 20)]
    [InlineData(19, 19, 21, 21, 10, 10, 25, 23)]
    [InlineData(9, 9, 11, 21, 15, 10, 20, 20)]
    [InlineData(9, 9, 21, 21, 15, 13, 25, 23)]
    public void Stretch_RectangleCornerEdgeAndAllCorners(double left, double bottom, double right, double top, double x1, double y1, double x2, double y2)
    {
        JObject result = Stretch(Rectangle(), [left, bottom, right, top], [5, 3]);
        Point([x1, y1, x2, y2], result["predicted"]![0]!["bounds"]!);
    }

    [Fact]
    public void Stretch_AllRectangleCorners_MoveExactlyOnceWhenOffsetMatchesWidth()
    {
        JObject result = Stretch(Rectangle(), [0, 0, 30, 30], [10, 10]);
        Point([20, 20, 30, 30], result["predicted"]![0]!["bounds"]!);
    }

    [Fact]
    public void Stretch_OneLineEndpoint_LeavesOtherUnchanged()
    {
        JObject result = Stretch(Line([10, 10], [20, 20]), [9, 9, 11, 11], [5, 3]);
        Point([15, 13], result["predicted"]![0]!["points"]![0]!);
        Point([20, 20], result["predicted"]![0]!["points"]![1]!);
    }

    [Fact]
    public void Stretch_EmptyRegionRotatedRectangleAndZeroArea_Reject()
    {
        Invalid(() => Stretch(Rectangle(), [0, 0, 1, 1], [5, 3]));
        JObject rotated = Rectangle();
        rotated["angle"] = 0.1;
        Invalid(() => Stretch(rotated, [9, 9, 21, 21], [5, 3]));
        Invalid(() => Stretch(Rectangle(), [9, 9, 11, 21], [10, 3]));
        Invalid(() => Stretch(Rectangle(), [20, 20, 10, 10], [5, 3]));
    }

    [Fact]
    public void MissingGeometry_WarnsAndLeavesPredictionUnchanged()
    {
        JObject row = Line([10, 10], [20, 20]);
        row.Remove("points");
        JObject result = GraphicEditGeometry.Plan(new JObject { ["operation"] = "set_length", ["length_mm"] = 15 }, new JArray(row));
        Assert.Contains("unreadable", (string)result["warning"]!, StringComparison.Ordinal);
        Assert.True(JToken.DeepEquals(row, result["predicted"]![0]));
        Assert.Empty(result["created"]!);
    }

    private static JObject Stretch(JObject row, double[] region, double[] offset) => GraphicEditGeometry.Plan(new JObject { ["operation"] = "stretch", ["region_mm"] = new JArray(region), ["offset_mm"] = new JArray(offset) }, new JArray(row));
    private static JObject Rectangle() => new() { ["kind"] = "Rectangle", ["angle"] = 0d, ["bounds"] = new JArray(10, 10, 20, 20), ["location"] = new JArray(10, 10) };
    private static JObject Line(double[] start, double[] end) => new() { ["kind"] = "Line", ["points"] = new JArray(new JArray(start), new JArray(end)), ["location"] = new JArray(start), ["bounds"] = new JArray(Math.Min(start[0], end[0]), Math.Min(start[1], end[1]), Math.Max(start[0], end[0]), Math.Max(start[1], end[1])) };
    private static void Point(double[] expected, JToken actual) { double[] values = actual.Values<double>().ToArray(); Assert.Equal(expected.Length, values.Length); for (int i = 0; i < expected.Length; i++) { Assert.Equal(expected[i], values[i], 7); } }
    private static double[] ArcPoint(JToken arc, double angle) => [(double)arc["position"]![0]! + (double)arc["radiusMm"]! * Math.Cos(angle * Math.PI / 180), (double)arc["position"]![1]! + (double)arc["radiusMm"]! * Math.Sin(angle * Math.PI / 180)];
    private static double Distance(double[] a, double[] b) => Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2));
    private static void Invalid(Action action) => Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(action).Error.Reason);
}

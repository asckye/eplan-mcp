using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class GraphicEditPlannerTests
{
    public static TheoryData<string> Operations => new("scale", "order", "group", "ungroup", "explode_block", "dock_text", "undock_text", "corner_radius", "copy_format", "cut_off", "set_length", "stretch", "snap_to_grid", "chamfer", "fillet");

    [Fact]
    public void PropertyChildLocatorReportsSupportedKindsInsteadOfStale()
    {
        (JObject input, JObject scene) = Request("order");
        JObject child = (JObject)scene["objects"]![0]!.DeepClone();
        child["kind"] = "PropertyPlacement";
        child["locator"]!["oid"] = 99;
        scene["objects"]![0]!["children"] = new JArray(child);
        input["locators"] = new JArray(child["locator"]!.DeepClone());
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => GraphicEditPlanner.Plan(input, scene));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains("Property text", error.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryOperation_PlansWithoutMutatingEvidence(string operation)
    {
        (JObject input, JObject scene) = Request(operation);
        JObject before = (JObject)scene.DeepClone();
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.Equal(operation, (string?)plan["operation"]);
        Assert.NotEmpty((string)plan["fingerprint"]!);
        Assert.NotNull(plan["predicted"]);
        Assert.NotNull(plan["noOp"]);
        Assert.True(JToken.DeepEquals(before, scene));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryOperation_RejectsChangedFingerprint(string operation)
    {
        (JObject input, JObject scene) = Request(operation);
        string fingerprint = (string)GraphicEditPlanner.Plan(input, scene)["fingerprint"]!;
        scene["scene"]!["page"]!["gridSize"] = 2;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => GraphicEditPlanner.Plan(input, scene, fingerprint));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryOperation_RejectsInvalidParameters(string operation)
    {
        (JObject input, JObject scene) = Request(operation);
        switch (operation)
        {
            case "scale":
                input.Remove("origin_mm");
                break;
            case "order":
                input["drawing_order"] = 127;
                break;
            case "group":
            case "dock_text":
                ((JArray)input["locators"]!).RemoveAt(1);
                break;
            case "ungroup":
                scene["objects"]![1]!["group"] = null;
                break;
            case "explode_block":
                scene["objects"]![0]!["kind"] = "Line";
                break;
            case "undock_text":
                scene["objects"]![0]!["kind"] = "Rectangle";
                break;
            case "corner_radius":
                input["radius_mm"] = -1;
                break;
            case "copy_format":
                input.Remove("source");
                break;
            case "cut_off":
                input["point_mm"] = new JArray(55, 60);
                break;
            case "set_length":
                input["length_mm"] = 0;
                break;
            case "stretch":
                input["region_mm"] = new JArray(0, 0, 1, 1);
                break;
            case "snap_to_grid":
                scene["objects"]![0]!["kind"] = "Function";
                break;
            case "chamfer":
                input["distance_mm"] = 0;
                break;
            case "fillet":
                input["radius_mm"] = 0;
                break;
        }
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
    }

    [Fact]
    public void Scale_ReproducesMeasuredOriginExample()
    {
        JObject scene = Scene(Rectangle(120, 40, 160, 80));
        JObject input = Input("scale", scene, 0);
        input["scale_x"] = 2;
        input["scale_y"] = .5;
        input["origin_mm"] = new JArray(120, 40);
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.Equal([120d, 40d, 200d, 60d], plan["predicted"]![0]!["bounds"]!.Values<double>());
    }

    [Fact]
    public void CutOff_ReproducesMeasuredPointAndIntersection()
    {
        (JObject input, JObject scene) = Request("cut_off");
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.Equal([70d, 40d], plan["predicted"]![0]!["points"]![0]!.Values<double>());
        Assert.Equal([100d, 40d], plan["predicted"]![0]!["points"]![1]!.Values<double>());
        Assert.Single(plan["affected"]!);
    }

    [Fact]
    public void CutOff_LastSegmentReportsRemoval_AndAmbiguousPointIsRejected()
    {
        JObject scene = Scene(Line([40, 40], [100, 40]));
        JObject input = Input("cut_off", scene, 0);
        input["point_mm"] = new JArray(55, 40);
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.Empty(plan["predicted"]!);
        Assert.Contains("last segment", (string)plan["warning"]!, StringComparison.Ordinal);
        (input, scene) = Request("cut_off");
        input["point_mm"] = new JArray(70, 40);
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
    }

    [Fact]
    public void CutOff_InteriorSegmentProducesBothRetainedSides()
    {
        JObject scene = Scene(Line([40, 40], [100, 40]), Line([60, 30], [60, 50]), Line([80, 30], [80, 50]));
        JObject input = Input("cut_off", scene, 0);
        input["point_mm"] = new JArray(70, 40);
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.Equal(2, plan["predicted"]!.Count());
        Assert.Equal([60d, 40d], plan["predicted"]![0]!["points"]![1]!.Values<double>());
        Assert.Equal([80d, 40d], plan["predicted"]![1]!["points"]![0]!.Values<double>());
    }

    [Theory]
    [InlineData("Circle", 0, 5, 5)]
    [InlineData("Ellipse", 0, 10, 5)]
    [InlineData("Ellipse", 1.5707963267948966, 5, 10)]
    public void CutOff_KnownCurveProximityIsRejected(string kind, double angle, double rx, double ry)
    {
        JObject scene = Scene(Line([40, 40], [100, 40]), Rectangle(40, 30, 70, 60));
        JToken curve = scene["objects"]![1]!;
        curve["kind"] = "Arc";
        curve["location"] = new JArray(55, 45);
        curve["properties"] = new JObject { ["arc_kind"] = kind, ["radius_mm"] = "5", ["radius_x_mm"] = rx.ToString(System.Globalization.CultureInfo.InvariantCulture), ["radius_y_mm"] = ry.ToString(System.Globalization.CultureInfo.InvariantCulture), ["ellipse_angle"] = angle.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        JObject input = Input("cut_off", scene, 0);
        input["point_mm"] = new JArray(55, 40);
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
    }

    [Fact]
    public void CutOff_ArcOutsideSweepAndMissingSegmentsWarnInsteadOfRefusing()
    {
        JObject scene = Scene(Line([40, 40], [100, 40]), Rectangle(50, 40, 60, 50));
        JToken arc = scene["objects"]![1]!;
        arc["kind"] = "Arc";
        arc["location"] = new JArray(55, 45);
        arc["properties"] = new JObject { ["arc_kind"] = "Arc", ["radius_mm"] = "5", ["start_angle"] = "0", ["end_angle"] = "90" };
        JObject input = Input("cut_off", scene, 0);
        input["point_mm"] = new JArray(55, 40);
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.True((bool)plan["unverifiedCut"]!);
        Assert.Contains("unverified", (string)plan["warning"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void CutOff_ClosedPolylineIncludesClosingSegment()
    {
        JObject scene = Scene(Line([40, 40], [100, 40]), Line([55, 30], [80, 30]));
        JToken polyline = scene["objects"]![1]!;
        polyline["kind"] = "PolyLine";
        polyline["points"] = new JArray(new JArray(55, 30), new JArray(80, 30), new JArray(55, 50));
        polyline["properties"]!["closed"] = "true";
        JObject input = Input("cut_off", scene, 0);
        input["point_mm"] = new JArray(55, 40);
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
    }

    [Theory]
    [InlineData("scale")]
    [InlineData("snap_to_grid")]
    public void GroupGeometryPredictsMembersAndRejectsDirectMemberEdits(string operation)
    {
        JObject scene = GroupScene();
        JObject input = Input(operation, scene, 0);
        if (operation == "scale")
        { input["scale_x"] = 2; input["scale_y"] = 2; input["origin_mm"] = new JArray(20, 20); }
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.Equal(2, plan["predicted"]!.Count());
        Assert.Equal(2, plan["affected"]!.Count());
        input["locators"] = new JArray(scene["objects"]![1]!["locator"]!.DeepClone());
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
    }

    [Fact]
    public void Scale_RejectsSymbolNestedInGroup()
    {
        JObject scene = GroupScene();
        scene["objects"]![1]!["kind"] = "Function";
        JObject input = Input("scale", scene, 0);
        input["scale_x"] = 2;
        input["scale_y"] = 2;
        input["origin_mm"] = new JArray(20, 20);
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
    }

    [Fact]
    public void CopyFormat_SourceAndTargetsMustBeGraphical()
    {
        (JObject input, JObject scene) = Request("copy_format");
        scene["objects"]![1]!["graphical"] = false;
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
        scene["objects"]![1]!["graphical"] = null;
        Assert.Contains("classification", (string)GraphicEditPlanner.Plan(input, scene)["warning"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void Snap_TiesAwayFromZero_AndAlreadySnappedIsNoOp()
    {
        JObject scene = Scene(Line([20.5, 20.5], [30.5, 20.5]));
        JObject input = Input("snap_to_grid", scene, 0);
        Assert.Equal([21d, 21d], GraphicEditPlanner.Plan(input, scene)["predicted"]![0]!["location"]!.Values<double>());
        scene = Scene(Line([20, 20], [30, 20]));
        input = Input("snap_to_grid", scene, 0);
        Assert.True((bool)GraphicEditPlanner.Plan(input, scene)["noOp"]!);
    }

    [Fact]
    public void Snap_MissingEndpointsWarnsSkipsAndPreservesOriginalGeometry()
    {
        JObject scene = Scene(Line([20.5, 20.5], [30.5, 20.5]), Rectangle(300.5, 300.5, 310.5, 310.5));
        scene["objects"]![0]!["points"] = null;
        JObject input = Input("snap_to_grid", scene, 0);
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.True((bool)plan["noOp"]!);
        Assert.Single(plan["skipped"]!);
        Assert.True(JToken.DeepEquals(scene["objects"]![0], plan["predicted"]![0]));
        input = Input("snap_to_grid", scene, 0, 1);
        plan = GraphicEditPlanner.Plan(input, scene);
        Assert.False((bool)plan["noOp"]!);
        Assert.Single(plan["skipped"]!);
        Assert.True(JToken.DeepEquals(scene["objects"]![0], plan["predicted"]![0]));
        Assert.Equal([301d, 301d], plan["predicted"]![1]!["location"]!.Values<double>());
    }

    [Fact]
    public void MissingGeometry_WarnsButStillRequiresValidParameters()
    {
        (JObject input, JObject scene) = Request("set_length");
        scene["objects"]![0]!["points"] = null;
        JObject plan = GraphicEditPlanner.Plan(input, scene);
        Assert.Contains("unreadable", (string)plan["warning"]!, StringComparison.Ordinal);
        Assert.True((bool)plan["noOp"]!);
        input.Remove("length_mm");
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
        (input, scene) = Request("scale");
        scene["objects"]![0]!["bounds"] = null;
        Assert.Contains("unreadable", (string)GraphicEditPlanner.Plan(input, scene)["warning"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommonInputs_RejectTypesDuplicatesAndUnknownMembers()
    {
        (JObject input, JObject scene) = Request("order");
        input["operation"] = new JObject();
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
        input["operation"] = "order";
        input["locators"] = new JArray(scene["objects"]![0]!["locator"]!.DeepClone(), scene["objects"]![0]!["locator"]!.DeepClone());
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
        input["locators"] = new JArray(scene["objects"]![0]!["locator"]!.DeepClone());
        input["extra"] = true;
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
        input.Remove("extra");
        input["dry_run"] = "false";
        Invalid(() => GraphicEditPlanner.Plan(input, scene));
    }

    internal static (JObject Input, JObject Scene) Request(string operation)
    {
        JObject scene = Scene(Line([40, 40], [100, 40]), Line([40, 40], [40, 100]));
        JObject input = Input(operation, scene, operation is "group" or "chamfer" or "fillet" ? [0, 1] : [0]);
        switch (operation)
        {
            case "scale":
                input["scale_x"] = 1.1;
                input["scale_y"] = 1.1;
                input["origin_mm"] = new JArray(40, 40);
                break;
            case "order":
                input["to"] = "front";
                break;
            case "ungroup":
                scene = GroupScene();
                input = Input(operation, scene, 1);
                break;
            case "explode_block":
                scene["objects"]![0]!["kind"] = "Block";
                scene["objects"]![0]!["memberStates"] = new JArray();
                break;
            case "dock_text":
            case "undock_text":
                scene["objects"]![0]!["kind"] = "Text";
                scene["objects"]![1]!["kind"] = "Text";
                if (operation == "dock_text")
                { input["locators"] = new JArray(scene["objects"]![0]!["locator"]!.DeepClone(), scene["objects"]![1]!["locator"]!.DeepClone()); }
                break;
            case "corner_radius":
                scene["objects"]![0]!["kind"] = "Rectangle";
                input["radius_mm"] = 2;
                break;
            case "copy_format":
                input["source"] = scene["objects"]![1]!["locator"]!.DeepClone();
                break;
            case "cut_off":
                scene = Scene(Line([40, 40], [100, 40]), Line([70, 30], [70, 50]));
                input = Input(operation, scene, 0);
                input["point_mm"] = new JArray(55, 40);
                break;
            case "set_length":
                input["length_mm"] = 40;
                break;
            case "stretch":
                input["region_mm"] = new JArray(39, 39, 41, 41);
                input["offset_mm"] = new JArray(5, 3);
                break;
            case "chamfer":
                input["distance_mm"] = 5;
                break;
            case "fillet":
                input["radius_mm"] = 5;
                break;
        }
        return (input, scene);
    }
    private static JObject GroupScene()
    {
        JObject scene = Scene(Rectangle(20.5, 20.5, 30.5, 30.5), Line([20.5, 20.5], [30.5, 30.5]));
        scene["objects"]![0]!["kind"] = "Group";
        scene["objects"]![0]!["graphical"] = false;
        scene["objects"]![1]!["group"] = scene["objects"]![0]!["locator"]!.DeepClone();
        return scene;
    }
    private static JObject Scene(params JObject[] rows)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i]["locator"] = new JObject { ["project"] = "project", ["page"] = "P", ["type"] = 4, ["oid"] = i + 1, ["digest"] = new string('a', 64) };
            rows[i]["drawing"] = rows[i]["locator"]!.DeepClone();
            rows[i]["children"] = new JArray();
            rows[i]["group"] = null;
            rows[i]["properties"] = new JObject();
            rows[i]["graphical"] = true;
            rows[i]["drawingOrder"] = 64;
            rows[i]["pen"] = new JObject { ["color"] = -16002 };
            rows[i]["layer"] = 1;
        }
        var scene = new ConnectionCreatePlanner.Scene { Project = new() { Name = "Line1", Path = "project" }, Page = new() { Project = "project", Page = "P", PageSize = [1000, 1000], GridSize = 1 }, Complete = true };
        scene.Page.Placements = rows.Select(r => r.ToObject<DrawingPlacement>(BridgeJson.Serializer)!).ToList();
        return new JObject { ["scene"] = BridgeJson.FromObject(scene), ["objects"] = new JArray(rows), ["functions"] = new JArray(), ["connections"] = new JArray() };
    }
    private static JObject Input(string operation, JObject scene, params int[] selected) => new() { ["project"] = "Line1", ["page"] = "P", ["operation"] = operation, ["locators"] = new JArray(selected.Select(i => scene["objects"]![i]!["locator"]!.DeepClone())), ["response_format"] = "detailed" };
    private static JObject Line(double[] start, double[] end) => new() { ["kind"] = "Line", ["points"] = new JArray(new JArray(start), new JArray(end)), ["location"] = new JArray(start), ["bounds"] = new JArray(Math.Min(start[0], end[0]), Math.Min(start[1], end[1]), Math.Max(start[0], end[0]), Math.Max(start[1], end[1])) };
    private static JObject Rectangle(double left, double bottom, double right, double top) => new() { ["kind"] = "Rectangle", ["location"] = new JArray(left, bottom), ["bounds"] = new JArray(left, bottom, right, top), ["angle"] = 0d };
    private static void Invalid(Action action) => Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(action).Error.Reason);
}

using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Pure geometry; unrecorded native behaviour is checked by PlacementEditSpikeTests, never inferred from success.</summary>
public static class PlacementEditPlanner
{
    public const string Tool = "eplan_placement_edit";
    private static readonly string[] _directions = ["Right", "Up", "Left", "Down"];
    public static JObject Plan(JObject input, JObject before, string? fingerprint = null)
    {
        string op = (string?)input["operation"] ?? "";
        Require(input.ToString(Formatting.None).Length <= 65536, "Keep the request within 64 KiB.");
        string optional = op switch { "delete" => "", "copy" => "offset_mm target_mm copies step_mm", "rotate" => "quarter_turns pivot_mm", "mirror" => "axis coordinate_mm", "align" => "alignment coordinate_mm", "distribute" => "axis", _ => throw Invalid("Use an operation from the schema enum.") };
        ConnectionCreatePlanner.Members(input, "", "project page operation locators", "dry_run response_format " + optional);
        ConnectionCreatePlanner.FiniteNumbers(input, "");
        Require(input["project"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["project"]) && input["page"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["page"]), "Specify an open project and exact page.");
        Require(input["dry_run"] is null || input["dry_run"]!.Type == JTokenType.Boolean, "dry_run must be boolean.");
        Require(input["response_format"] is null || input["response_format"]!.Type == JTokenType.String && (string?)input["response_format"] is "concise" or "detailed", "Use concise or detailed.");
        Require(input["locators"] is JArray { Count: >= 1 and <= 50 }, "Supply 1..50 locators from eplan_object_find.");
        var scene = before["scene"]!.ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
        Require((string?)input["page"] == scene.Page.Page && ((string?)input["project"] == scene.Project.Name || ProjectResolver.SamePath((string)input["project"]!, scene.Project.Path)), "All locators must belong to the explicit project/page.");
        if (!scene.Complete || scene.PageType != "Circuit" || scene.Page.Placements.Count > 10000 || !DrawingCoordinates.Contains(scene.Page.PageSize, scene.Page.Placements.Select(p => p.Bounds)))
        { throw Missing("Read a complete Circuit page with reliable bounds."); }
        JToken[] objects = before["objects"]!.ToArray();
        var selected = new List<JToken>();
        foreach (JToken locator in input["locators"]!)
        {
            ConnectionCreatePlanner.Members(locator, "/locators", "project page type oid digest", "");
            Require(locator["project"]?.Type == JTokenType.String && locator["page"]?.Type == JTokenType.String && locator["type"]?.Type == JTokenType.Integer && locator["oid"]?.Type == JTokenType.Integer && locator["digest"]?.Type == JTokenType.String, "Supply complete find locators without coercion.");
            JToken found = objects.SingleOrDefault(o => JToken.DeepEquals(o["locator"], locator)) ?? throw Stale();
            Require(!selected.Contains(found), "Remove duplicate locators.");
            if ((string?)found["properties"]?["grouped"] != "false" || (string?)found["kind"] is not ("Function" or "Text" or "Line" or "Rectangle" or "SymbolReference" or "PotentialDefinition"))
            { throw Missing("Groups, property placements and this placement kind cannot be edited."); }
            selected.Add(found);
        }
        string hash = Fingerprint.Of([input.ToString(Formatting.None), before.ToString(Formatting.None)]);
        hash = fingerprint is null || fingerprint == hash ? hash : throw Stale();
        int axis = ((string?)input["axis"] == "vertical") == (op != "distribute") ? 0 : 1;
        string alignment = (string?)input["alignment"] ?? "";
        Require(op is not ("mirror" or "distribute") || input["axis"]?.Type == JTokenType.String && (string?)input["axis"] is "horizontal" or "vertical", "Use horizontal or vertical axis.");
        if (op == "align")
        { Require(input["alignment"]?.Type == JTokenType.String && alignment is "left" or "right" or "top" or "bottom" or "centre_x" or "centre_y", "Use an alignment from the schema enum."); axis = alignment is "left" or "right" or "centre_x" ? 0 : 1; }
        Require(input["coordinate_mm"] is not { } coordinate || coordinate.Type is JTokenType.Integer or JTokenType.Float && Math.Abs((double)coordinate) <= 10000, "Use a finite coordinate in ±10000 mm.");
        Require(op != "mirror" || input["coordinate_mm"] is not null, "Mirror requires coordinate_mm.");
        int turns = op == "rotate" && input["quarter_turns"]?.Type == JTokenType.Integer && input["quarter_turns"]!.Value<double>() is >= -3 and <= 3 ? (int)input["quarter_turns"]! : 0;
        Require(op != "rotate" || turns is >= -3 and <= 3 && turns != 0, "Use quarter_turns ±1..±3.");
        double[]? pivot = input["pivot_mm"] is { } pivotToken ? Point(pivotToken) : null;
        int copies = input["copies"] is null ? 1 : input["copies"]!.Type == JTokenType.Integer && input["copies"]!.Value<double>() is >= 1 and <= 20 ? (int)input["copies"]! : 0;
        Require(copies is >= 1 and <= 20 && copies * selected.Count <= 50, "Keep copies in 1..20 and new roots within 50.");
        double[] offset = [0, 0], step = input["step_mm"] is { } s ? Point(s) : [0, 0];
        if (op == "copy")
        {
            Require((input["offset_mm"] is null) != (input["target_mm"] is null) && (copies == 1 || input["step_mm"] is not null), "Copy needs offset_mm or target_mm; multiple copies also need step_mm.");
            offset = Point(input["offset_mm"] ?? input["target_mm"]!);
            offset = input["target_mm"] is null ? offset : offset.Zip(selected[0]["location"]!.Values<double>(), (a, b) => a - b).ToArray();
        }
        double Edge(JToken o) => alignment.StartsWith("centre", StringComparison.Ordinal) ? ((double)o["bounds"]![axis]! + (double)o["bounds"]![axis + 2]!) / 2 : (double)o["bounds"]![axis + (alignment is "right" or "top" ? 2 : 0)]!;
        var moves = new JArray();
        if (op is "align" or "distribute")
        {
            Require(selected.Count >= (op == "distribute" ? 3 : 2), "Select at least two objects for alignment or three for distribution.");
            JToken[] sorted = selected.OrderBy(o => (double)o["bounds"]![axis]!).ToArray();
            double gap = ((double)sorted[sorted.Length - 1]["bounds"]![axis + 2]! - (double)sorted[0]["bounds"]![axis]! - sorted.Sum(o => (double)o["bounds"]![axis + 2]! - (double)o["bounds"]![axis]!)) / (sorted.Length - 1);
            Require(op != "distribute" || gap >= 0, "The selected span cannot fit nonoverlapping equal gaps.");
            double next = (double)sorted[0]["bounds"]![axis]!;
            foreach (JToken item in op == "distribute" ? sorted : selected.ToArray())
            {
                double[] at = item["location"]!.Values<double>().ToArray();
                at[axis] += op == "align" ? ((double?)input["coordinate_mm"] ?? Edge(selected[0])) - Edge(item) : next - (double)item["bounds"]![axis]!;
                next += (double)item["bounds"]![axis + 2]! - (double)item["bounds"]![axis]! + gap;
                moves.Add(new JObject { ["locator"] = item["drawing"]!.DeepClone(), ["x_mm"] = at[0], ["y_mm"] = at[1] });
            }
        }
        var moveInput = new JObject { ["project"] = input["project"]!.DeepClone(), ["page"] = input["page"]!.DeepClone(), ["moves"] = moves, ["snap_to_grid"] = false, ["response_format"] = "detailed" };
        JObject? movePlan = moves.Count == 0 ? null : PlacementMovePlanner.Plan(moveInput.ToString(Formatting.None), scene);
        var predicted = new JArray();
        foreach (JToken root in selected)
        {
            for (int n = 0; n < (op == "copy" ? copies : 1); n++)
            {
                JObject item = (JObject)root.DeepClone();
                double[] delta = op == "copy" ? offset.Zip(step, (a, b) => a + n * b).ToArray() : moves.FirstOrDefault(m => JToken.DeepEquals(m["locator"], root["drawing"])) is { } move ? new[] { (double)move["x_mm"]!, (double)move["y_mm"]! }.Zip(root["location"]!.Values<double>(), (a, b) => a - b).ToArray() : [0, 0];
                double[] centre = pivot ?? root["location"]!.Values<double>().ToArray();
                double[] Transform(double[] p)
                {
                    p[axis] = op == "mirror" ? 2 * (double)input["coordinate_mm"]! - p[axis] : p[axis];
                    for (int k = 0; k < (turns + 4) % 4; k++)
                    { p = [centre[0] - p[1] + centre[1], centre[1] + p[0] - centre[0]]; }
                    return p.Zip(delta, (a, b) => Math.Round(a + b, 8)).ToArray();
                }
                foreach (JObject row in new[] { item }.Concat(item["children"]!.Cast<JObject>()))
                {
                    if (op is "rotate" or "mirror" && (string?)row["kind"] is "Text" or "PropertyPlacement" && row["angle"]?.Type == JTokenType.Null)
                    { throw Missing("Text angle inherited from its layer is not verified; set an explicit angle before transforming."); }
                    row["location"] = new JArray(Transform(row["location"]!.Values<double>().ToArray()));
                    double[] b = row["bounds"]!.Values<double>().ToArray();
                    double[][] corners = new[] { new[] { b[0], b[1] }, [b[2], b[1]], [b[2], b[3]], [b[0], b[3]] }.Select(Transform).ToArray();
                    row["bounds"] = b.All(v => v == 0) ? new JArray(b) : new JArray(corners.Min(p => p[0]), corners.Min(p => p[1]), corners.Max(p => p[0]), corners.Max(p => p[1]));
                    foreach (JProperty pin in ((JObject?)row["pins"] ?? new()).Properties())
                    { pin.Value["location"] = new JArray(Transform(pin.Value["location"]!.Values<double>().ToArray())); int d = Array.IndexOf(_directions, (string)pin.Value["direction"]!); Require(d >= 0, "Pins need cardinal directions."); pin.Value["direction"] = _directions[((op == "mirror" ? axis == 0 ? 2 - d : -d : d) + turns + 8) % 4]; }
                    if (row["points"] is JArray points)
                    { row["points"] = new JArray(points.Select(p => new JArray(Transform(p.Values<double>().ToArray())))); }
                    if (row["angle"]?.Type is JTokenType.Float or JTokenType.Integer)
                    { double angle = (double)row["angle"]!; row["angle"] = Normalize((op == "mirror" ? axis == 0 ? Math.PI - angle : -angle : angle) + turns * Math.PI / 2); }
                    Require(op == "delete" || DrawingCoordinates.Contains(scene.Page.PageSize, [row["bounds"]!.Values<double>().All(v => v == 0) ? row["location"]!.Values<double>().Concat(row["location"]!.Values<double>()).ToArray() : row["bounds"]!.Values<double>().ToArray()]), "Keep transformed outlines inside Page.Size.");
                }
                item["exists"] = op != "delete";
                predicted.Add(item);
            }
        }
        JToken[] connections = before["connections"]!.Where(c => op == "delete" && selected.Any(o => new[] { c["start"], c["end"] }.Any(e => e is JObject && Id(e) == Id(o["locator"]!)))).ToArray();
        foreach (JToken item in op is "copy" or "rotate" or "mirror" ? predicted : new JArray())
        {
            double[] a = item["bounds"]!.Values<double>().ToArray();
            Require(!objects.Where(o => op == "copy" || !selected.Contains(o)).Concat(predicted.Where(p => p != item)).Any(o => o["bounds"]!.Values<double>().ToArray() is var b && a[0] < b[2] && a[2] > b[0] && a[1] < b[3] && a[3] > b[1]), "A predicted outline overlaps another placement.");
        }
        string output = predicted.ToString(Formatting.None) + new JArray(connections).ToString(Formatting.None) + new JArray(op == "copy" ? predicted.Select(p => p["locator"]!.DeepClone()) : []).ToString(Formatting.None);
        Require((output.Count(c => c <= 127) + 3) / 4 + output.Count(c => c > 127) < ((string?)input["response_format"] == "detailed" ? 19000 : 4500), "Use detailed or fewer objects; complete effects cannot be truncated.");
        return new JObject { ["project"] = scene.Project.Name, ["page"] = scene.Page.Page, ["operation"] = op, ["fingerprint"] = hash, ["predicted"] = predicted, ["removedConnections"] = new JArray(connections.Select(c => c.DeepClone())), ["newLocators"] = new JArray(), ["moveInput"] = moveInput, ["movePlan"] = movePlan, ["noOp"] = movePlan?["noOp"] ?? false };
    }
    public static string Id(JToken o) => o["type"] + "/" + o["oid"];
    public static double Normalize(double angle) => (angle % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
    private static double[] Point(JToken p) { Require(p is JArray { Count: 2 } && p.All(n => n.Type is JTokenType.Float or JTokenType.Integer && Math.Abs((double)n) <= 10000), "Use two finite mm coordinates in ±10000."); return p.Values<double>().ToArray(); }
    private static void Require(bool condition, string message) { if (!condition) { throw Invalid(message); } }
    private static BridgeErrorException Invalid(string message) => WriteArguments.NotStarted(WriteArguments.Invalid([message]));
    private static BridgeErrorException Missing(string message) => WriteArguments.NotStarted(new(ErrorReason.CapabilityMissing, message));
    private static BridgeErrorException Stale() => WriteArguments.NotStarted(new(ErrorReason.PlanStale, "Object or page evidence changed; call eplan_object_find and preview again."));
}

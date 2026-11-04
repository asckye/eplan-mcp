using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A57: parent, displayed property and zero-based collection index identify each context text.</summary>
internal static class PropertyPlacementMoves
{
    internal static JObject Plan(JObject input, ConnectionCreatePlanner.Scene scene, string? expected)
    {
        ConnectionCreatePlanner.Members(input, "", "project page property_moves", "dry_run snap_to_grid response_format layout_guard");
        LayoutGuard.Mode(input["layout_guard"]);
        ConnectionCreatePlanner.FiniteNumbers(input, "");
        Require(input["project"]?.Type == JTokenType.String && ((string?)input["project"] == scene.Project.Name || ProjectResolver.SamePath((string)input["project"]!, scene.Project.Path))
            && (string?)input["page"] == scene.Page.Page, "Select this project's exact page.");
        foreach (string flag in new[] { "dry_run", "snap_to_grid" })
        { Require(input[flag] is null || input[flag]!.Type == JTokenType.Boolean, "Supply boolean flags."); }
        Require(input["response_format"] is null || input["response_format"]!.Type == JTokenType.String && (string?)input["response_format"] is "concise" or "detailed", "Use concise or detailed.");
        Require(input["property_moves"] is JArray { Count: >= 1 and <= 50 }, "Supply 1..50 property moves.");
        Require(scene.Complete && scene.PageType == "Circuit" && scene.Page.Project == scene.Project.Path && DrawingCoordinates.ValidBounds([0, 0, .. scene.Page.PageSize ?? []]) && scene.Page.PageSize!.All(n => n > 0)
            && scene.Page.Placements.Count <= 10000, "Read a complete Circuit page with Page.Size.");
        string fingerprint = Fingerprint.Of([input.ToString(Formatting.None), BridgeJson.FromObject(scene).ToString(Formatting.None)]);
        if (expected is not null && fingerprint != expected)
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Page or property text changed; read the drawings and re-plan."); }
        var rows = new JArray();
        foreach (JToken move in input["property_moves"]!)
        {
            ConnectionCreatePlanner.Members(move, "/property_moves", "parent property_id property_index x_mm y_mm angle_deg", "alignment height_mm visible layer pen");
            var style = new JObject();
            foreach (string name in new[] { "alignment", "height_mm", "visible", "layer", "pen" })
            { if (((JObject)move).ContainsKey(name)) { style[name] = move[name]!.DeepClone(); } }
            Require(style["alignment"] is null || style["alignment"]!.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)style["alignment"]), "Use an EPLAN alignment name.");
            Require(style["height_mm"] is null || style["height_mm"]!.Type is JTokenType.Integer or JTokenType.Float && ((double)style["height_mm"]! == -16002 || (double)style["height_mm"]! is >= 0.5 and <= 100), "Use height_mm 0.5..100 or -16002.");
            Require(style["visible"] is null || style["visible"]!.Type == JTokenType.Boolean, "Use boolean visible.");
            Require(style["layer"] is null || style["layer"]!.Type == JTokenType.Integer && int.TryParse(style["layer"]!.ToString(), out int layer) && layer >= 0, "Use an integer layer ID.");
            if (style["pen"] is { } pen)
            {
                ConnectionCreatePlanner.Members(pen, "/property_moves/pen", "", "color width_mm style pattern_length line_end");
                Require(((JObject)pen).Properties().All(p => p.Value.Type is JTokenType.Integer or JTokenType.Float && PlacementTextCreateExecuteOperation.ValidPen(p.Name, (double)p.Value)), "Use finite pen values and valid IDs, or -16002.");
            }
            ConnectionCreatePlanner.Members(move["parent"], "/property_moves/parent", "project page type oid digest", "");
            JToken given = move["parent"]!;
            Require(given["project"]?.Type == JTokenType.String && given["page"]?.Type == JTokenType.String
                && given["type"]?.Type == JTokenType.Integer && int.TryParse(given["type"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int type) && type > 0
                && given["oid"]?.Type == JTokenType.Integer && long.TryParse(given["oid"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long oid) && oid > 0
                && given["digest"]?.Type == JTokenType.String && ((string)given["digest"]!).Length == 64 && ((string)given["digest"]!).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Copy the complete parent locator with SHA-256 digest.");
            DrawingLocator parent = move["parent"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!;
            DrawingPlacement? owner = scene.Page.Placements.SingleOrDefault(p => p.Parent is null && DrawingLocators.Matches(parent, p.Locator));
            if (owner is null)
            { throw new BridgeErrorException(ErrorReason.PlanStale, "Parent locator changed; read eplan_page_get include_drawings again."); }
            Require(move["property_id"]?.Type == JTokenType.Integer && move["property_index"]?.Type == JTokenType.Integer
                && int.TryParse(move["property_id"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 && int.TryParse(move["property_index"]!.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index is >= 0 and <= 9999
                && move["angle_deg"]?.Type == JTokenType.Integer && move["angle_deg"]!.ToString() is "0" or "90" or "180" or "270" or "-16002", "Use integer property selectors and angle_deg 0/90/180/270, or -16002 for the layer's rotation.");
            DrawingPlacement? child = scene.Page.Placements.SingleOrDefault(p => p.Kind == "PropertyPlacement" && p.Parent is not null && DrawingLocators.Matches(parent, p.Parent)
                && Value(p, "property_id") == move["property_id"]!.ToString() && Value(p, "property_index") == move["property_index"]!.ToString());
            if (child is null)
            { throw new BridgeErrorException(ErrorReason.PlanStale, "Property selector changed; refresh the parent's property placements."); }
            Require(Value(owner, "grouped") == "false" && Value(child, "grouped") == "false"
                && child.Location.Length == 2 && DrawingCoordinates.ValidBounds(child.Bounds)
                && (Value(child, "angle_deg") == "from_layer" || double.TryParse(Value(child, "angle_deg"), NumberStyles.Float, CultureInfo.InvariantCulture, out double before) && before is 0 or 90 or 180 or 270),
                "Require ungrouped text with known bounds and a right-angle rotation.");
            Require(!rows.Any(r => DrawingLocators.Matches(child.Locator, r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!)), "Remove duplicate property targets.");
            JToken[] coordinates = [move["x_mm"]!, move["y_mm"]!];
            Require(coordinates.All(n => n.Type is JTokenType.Float or JTokenType.Integer && (double)n is >= 0 and <= 10000), "Supply finite page coordinates 0..10000 mm.");
            double[] requested = coordinates.Select(n => (double)n).ToArray();
            double[] after = requested.SequenceEqual(child.Location) ? requested : requested.Select(n => DrawingCoordinates.Snap(n, scene.Page.GridSize, (bool?)input["snap_to_grid"] ?? true)).ToArray();
            double angle = (double)move["angle_deg"]!;
            // The layer's rotation reads from_layer and turns the text like 0 degrees.
            double oldAngle = child.Properties["angle_deg"] == "from_layer" ? FromLayerAngle : double.Parse(child.Properties["angle_deg"], CultureInfo.InvariantCulture);
            int turns = ((int)((angle == FromLayerAngle ? 0 : angle) - (oldAngle == FromLayerAngle ? 0 : oldAngle)) + 360) / 90 % 4;
            double[][] corners = new[] { new[] { child.Bounds![0], child.Bounds[1] }, [child.Bounds[2], child.Bounds[1]], [child.Bounds[2], child.Bounds[3]], [child.Bounds[0], child.Bounds[3]] }
                .Select(p => { double x = p[0] - child.Location[0], y = p[1] - child.Location[1]; for (int i = 0; i < turns; i++) { (x, y) = (-y, x); } return new[] { x + after[0], y + after[1] }; }).ToArray();
            double[] bounds = child.Bounds!.All(n => n == 0) ? [0, 0, 0, 0] : [corners.Min(p => p[0]), corners.Min(p => p[1]), corners.Max(p => p[0]), corners.Max(p => p[1])];
            Require(DrawingCoordinates.Contains(scene.Page.PageSize, [bounds, [after[0], after[1], after[0], after[1]]]), "Keep the rotated text inside Page.Size.");
            rows.Add(new JObject
            {
                ["locator"] = BridgeJson.FromObject(child.Locator),
                ["parent"] = BridgeJson.FromObject(parent),
                ["propertyId"] = move["property_id"]!.DeepClone(),
                ["propertyIndex"] = move["property_index"]!.DeepClone(),
                ["before"] = new JArray(child.Location),
                ["requested"] = new JArray(requested),
                ["after"] = new JArray(after),
                ["snapped"] = !requested.SequenceEqual(after),
                ["bounds"] = new JArray(bounds),
                ["beforeAngle"] = oldAngle,
                ["angle"] = angle,
                ["style"] = style
            });
        }
        bool noOp = rows.All(r =>
        {
            DrawingPlacement child = scene.Page.Placements.Single(p => DrawingLocators.Matches(p.Locator, r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!));
            JObject style = (JObject)r["style"]!;
            return JToken.DeepEquals(r["before"], r["after"]) && (double)r["beforeAngle"]! == (double)r["angle"]! && Value(child, "docked") != "true"
                && (bool?)style["visible"] != false && StyleValues(style).All(p => Value(child, p.Key) == p.Value);
        });
        var result = new JObject
        {
            ["project"] = scene.Project.Name,
            ["page"] = scene.Page.Page,
            ["execution"] = ExecutionState.NotStarted,
            ["dryRun"] = true,
            ["noOp"] = noOp,
            ["gridSize"] = scene.Page.GridSize,
            ["boundarySource"] = "Page.Size",
            ["moves"] = rows,
            ["fingerprint"] = fingerprint,
            ["graphicalChanges"] = new JArray(),
            ["connectionChanges"] = new JArray(),
            ["generation"] = "Property text only; connections are unchanged.",
            ["warning"] = rows.Any(r => (bool?)r["style"]?["visible"] == false) ? "visible:false is written but not compared: EPLAN may read IsVisible=true when invisible elements are displayed." : null
        };
        Require(result.ToString(Formatting.None).Length <= ((string?)input["response_format"] == "detailed" ? 30000 : 7500), "Use detailed or fewer property moves.");
        return result;
    }

    internal const double FromLayerAngle = -16002;

    internal static Dictionary<string, string> StyleValues(JObject style) => style.Properties().SelectMany(p => p.Name == "pen"
        ? ((JObject)p.Value).Properties().Select(v => new KeyValuePair<string, string>("pen_" + v.Name, ((double)v.Value).ToString("R", CultureInfo.InvariantCulture)))
        : p.Name == "height_mm" ? new[] { new KeyValuePair<string, string>("height_raw_mm", ((double)p.Value).ToString("R", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("height_mm", (double)p.Value == -16002 ? "from_layer" : ((double)p.Value).ToString("R", CultureInfo.InvariantCulture)) }
        : new[] { new KeyValuePair<string, string>(p.Name, p.Value.Type == JTokenType.Boolean ? ((bool)p.Value).ToString().ToLowerInvariant() : p.Value.Type is JTokenType.Integer or JTokenType.Float ? ((double)p.Value).ToString("R", CultureInfo.InvariantCulture) : (string)p.Value!) }).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static string? Value(DrawingPlacement row, string key) => row.Properties.TryGetValue(key, out string? value) ? value : null;

    private static void Require(bool valid, string message)
    { if (!valid) { throw WriteArguments.Invalid(["/property_moves: " + message]); } }
}

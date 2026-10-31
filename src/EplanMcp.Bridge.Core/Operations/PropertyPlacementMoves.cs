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
        ConnectionCreatePlanner.Members(input, "", "project page property_moves", "dry_run snap_to_grid response_format");
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
            ConnectionCreatePlanner.Members(move, "/property_moves", "parent property_id property_index x_mm y_mm angle_deg", "");
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
                && move["angle_deg"]?.Type == JTokenType.Integer && move["angle_deg"]!.ToString() is "0" or "90" or "180" or "270", "Use integer property selectors and angle_deg 0/90/180/270.");
            DrawingPlacement? child = scene.Page.Placements.SingleOrDefault(p => p.Kind == "PropertyPlacement" && p.Parent is not null && DrawingLocators.Matches(parent, p.Parent)
                && Value(p, "property_id") == move["property_id"]!.ToString() && Value(p, "property_index") == move["property_index"]!.ToString());
            if (child is null)
            { throw new BridgeErrorException(ErrorReason.PlanStale, "Property selector changed; refresh the parent's property placements."); }
            Require(Value(owner, "grouped") == "false" && Value(child, "grouped") == "false"
                && child.Location.Length == 2 && DrawingCoordinates.ValidBounds(child.Bounds)
                && double.TryParse(Value(child, "angle_deg"), NumberStyles.Float, CultureInfo.InvariantCulture, out double before) && before is 0 or 90 or 180 or 270,
                "Require ungrouped text with known bounds and a right-angle rotation.");
            Require(!rows.Any(r => DrawingLocators.Matches(child.Locator, r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!)), "Remove duplicate property targets.");
            JToken[] coordinates = [move["x_mm"]!, move["y_mm"]!];
            Require(coordinates.All(n => n.Type is JTokenType.Float or JTokenType.Integer && (double)n is >= 0 and <= 10000), "Supply finite page coordinates 0..10000 mm.");
            double[] requested = coordinates.Select(n => (double)n).ToArray();
            double[] after = requested.SequenceEqual(child.Location) ? requested : requested.Select(n => DrawingCoordinates.Snap(n, scene.Page.GridSize, (bool?)input["snap_to_grid"] ?? true)).ToArray();
            double angle = (double)move["angle_deg"]!;
            double oldAngle = double.Parse(child.Properties["angle_deg"], CultureInfo.InvariantCulture);
            int turns = ((int)(angle - oldAngle) + 360) / 90 % 4;
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
                ["angle"] = angle
            });
        }
        bool noOp = rows.All(r => JToken.DeepEquals(r["before"], r["after"]) && (double)r["beforeAngle"]! == (double)r["angle"]! && !scene.Page.Placements.Any(p => DrawingLocators.Matches(p.Locator, r["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!) && Value(p, "docked") == "true"));
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
            ["generation"] = "Property text only; connections are unchanged."
        };
        Require(result.ToString(Formatting.None).Length <= ((string?)input["response_format"] == "detailed" ? 30000 : 7500), "Use detailed or fewer property moves.");
        return result;
    }

    private static string? Value(DrawingPlacement row, string key) => row.Properties.TryGetValue(key, out string? value) ? value : null;

    private static void Require(bool valid, string message)
    { if (!valid) { throw WriteArguments.Invalid(["/property_moves: " + message]); } }
}

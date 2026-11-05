using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public static class GraphicEditPlanner
{
    public const string Tool = "eplan_graphic_edit";
    private const string Graphics = "Line Rectangle Text Arc Circle Ellipse PolyLine Spline Group";
    public static JObject Plan(JObject input, JObject before, string? fingerprint = null)
    {
        Require(input["operation"]?.Type == JTokenType.String, "operation must be a string from the schema enum.");
        string op = (string?)input["operation"] ?? "";
        string fields = op switch
        {
            "scale" => "scale_x scale_y origin_mm layout_guard",
            "order" => "to drawing_order",
            "group" or "ungroup" or "explode_block" or "undock_text" or "snap_to_grid" => "",
            "dock_text" => "dock_direction",
            "corner_radius" => "radius_mm",
            "copy_format" => "source",
            "cut_off" => "point_mm",
            "set_length" => "length_mm anchor layout_guard",
            "stretch" => "region_mm offset_mm layout_guard",
            "chamfer" => "distance_mm",
            "fillet" => "radius_mm",
            _ => throw Invalid("Use an operation from the schema enum.")
        };
        Require(input.ToString(Formatting.None).Length <= 65536, "Keep the request within 64 KiB.");
        ConnectionCreatePlanner.Members(input, "", "project page operation locators", "dry_run response_format " + fields);
        ConnectionCreatePlanner.FiniteNumbers(input, "");
        Require(input["project"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["project"])
            && input["page"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)input["page"]), "Specify an open project and exact page.");
        Require(input["dry_run"] is null || input["dry_run"]!.Type == JTokenType.Boolean, "dry_run must be boolean.");
        Require(input["response_format"] is null || input["response_format"]!.Type == JTokenType.String && (string?)input["response_format"] is "concise" or "detailed", "Use concise or detailed.");
        Require(input["locators"] is JArray { Count: >= 1 and <= 50 }, "Supply 1..50 locators from eplan_object_find.");
        var scene = before["scene"]!.ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
        Require((string?)input["page"] == scene.Page.Page && ((string?)input["project"] == scene.Project.Name || ProjectResolver.SamePath((string)input["project"]!, scene.Project.Path)), "All locators must belong to the explicit project/page.");
        var selected = new JArray(input["locators"]!.Select(l => Resolve(l, before).DeepClone()));
        Require(selected.Select(o => Id(o["locator"]!)).Distinct(StringComparer.Ordinal).Count() == selected.Count, "Remove duplicate locators.");
        Require(op != "group" || selected.Count >= 2, "group requires 2..50 placements.");
        Require(op is not ("dock_text" or "chamfer" or "fillet") || selected.Count == 2, op + " requires exactly two placements.");
        Require(op != "cut_off" || selected.Count == 1, "cut_off requires exactly one line.");
        if (op is "set_length" or "chamfer" or "fillet")
        { Number(input, op == "set_length" ? "length_mm" : op == "chamfer" ? "distance_mm" : "radius_mm", double.Epsilon); }
        Require(op != "set_length" || input["anchor"] is null || input["anchor"]!.Type == JTokenType.String && (string?)input["anchor"] is "start" or "end", "anchor must be start or end.");
        if (op == "stretch")
        { double[] region = Point(input["region_mm"], 4); Point(input["offset_mm"]); Require(region[0] <= region[2] && region[1] <= region[3], "region_mm must be [left,bottom,right,top]."); }
        string allowed = op switch
        {
            "group" or "ungroup" => Graphics + " Function SymbolReference",
            "explode_block" => "Block",
            "dock_text" or "undock_text" => "Text",
            "corner_radius" => "Rectangle",
            "cut_off" or "set_length" or "chamfer" or "fillet" => "Line",
            "stretch" => "Line Rectangle",
            _ => Graphics
        };
        foreach (JToken row in selected)
        {
            string kind = (string?)row["kind"] ?? "";
            Require(allowed.Split(' ').Contains(kind) && (bool?)row["blockMember"] != true, op + " supports independent " + allowed + "; property text and block members are excluded.");
            Require(op == "ungroup" || row["group"] is null or { Type: JTokenType.Null }, "Select the Group, or ungroup its members first.");
        }
        if (op == "ungroup")
        {
            foreach (JToken row in selected)
            { Require((string?)row["kind"] == "Group" || row["group"] is JObject, "ungroup requires a group or one of its members."); }
        }
        if (op == "group")
        { Require(selected.All(r => r["group"] is null or { Type: JTokenType.Null }), "Ungroup existing groups before creating a group."); }
        if (op == "copy_format")
        {
            JToken source = Resolve(input["source"], before);
            Require((bool?)source["graphical"] != false && selected.All(r => (bool?)r["graphical"] != false), "copy_format requires GraphicalPlacement source and targets (Line Rectangle Text Arc Circle Ellipse PolyLine Spline).");
            Require((string?)source["kind"] != "PropertyPlacement" && (bool?)source["blockMember"] != true, "copy_format excludes property text and block members.");
        }
        if (op == "scale")
        { Require(Closure(selected, before, op).All(r => Graphics.Split(' ').Contains((string?)r["kind"])), "scale supports " + Graphics + "; groups containing symbols cannot be scaled."); }
        string hash = Fingerprint.Of([input.ToString(Formatting.None), before.ToString(Formatting.None)]);
        if (fingerprint is not null && hash != fingerprint)
        { throw Stale(); }
        JObject plan = GraphicEditPredictions.Plan(input, before, selected);
        plan["noOp"] ??= op is "set_length" or "stretch" or "chamfer" or "fillet" && !plan["created"]!.Any() && JToken.DeepEquals(selected, plan["predicted"]);
        if (op == "copy_format" && new[] { Resolve(input["source"], before) }.Concat(selected).Any(r => r["graphical"] is null or { Type: JTokenType.Null }))
        { Warn(plan, "GraphicalPlacement classification is unreadable; format support is unverified."); }
        plan["project"] = scene.Project.Name;
        plan["page"] = scene.Page.Page;
        plan["operation"] = op;
        plan["fingerprint"] = hash;
        plan["newLocators"] = new JArray();
        plan["affected"] = new JArray(Closure(selected, before, op).Select(r => r["locator"]!.DeepClone()));
        if (!scene.Complete)
        { Warn(plan, "The page inventory is incomplete; unavailable evidence is unverified."); }
        if (op is "scale" or "stretch" or "set_length")
        {
            var model = LayoutGuard.From(scene);
            model.AllowUnknownGeometry = true;
            DrawingPlacement LayoutRow(JToken state)
            {
                var row = state.ToObject<DrawingPlacement>(BridgeJson.Serializer)!;
                if (state["group"] is JObject group)
                { row.Parent = group.ToObject<DrawingLocator>(BridgeJson.Serializer); }
                return row;
            }
            model.Page.Placements = before["objects"]!.Select(LayoutRow).ToList();
            var changed = plan["predicted"]!.Select(LayoutRow).ToArray();
            Warn(plan, LayoutGuard.Summary(LayoutGuard.Enforce(model, changed, mode: LayoutGuard.Mode(input["layout_guard"]))));
        }
        string output = plan.ToString(Formatting.None);
        Require((output.Count(c => c <= 127) + 3) / 4 + output.Count(c => c > 127) < ((string?)input["response_format"] == "detailed" ? 19000 : 4500), "Use detailed or fewer objects; complete effects cannot be truncated.");
        return plan;
    }
    internal static IEnumerable<JToken> Closure(JArray selected, JObject before, string op)
    {
        var ids = new HashSet<string>(selected.Select(r => Id(r["locator"]!)), StringComparer.Ordinal);
        if (op == "ungroup")
        { ids.UnionWith(selected.Where(r => r["group"] is JObject).Select(r => Id(r["group"]!))); }
        if (op is not ("scale" or "ungroup" or "snap_to_grid"))
        { return before["objects"]!.Where(r => ids.Contains(Id(r["locator"]!))); }
        bool grew;
        do
        {
            int count = ids.Count;
            foreach (JToken row in before["objects"]!)
            {
                if (row["group"] is JObject group && ids.Contains(Id(group)))
                { ids.Add(Id(row["locator"]!)); }
            }
            grew = count != ids.Count;
        } while (grew);
        return before["objects"]!.Where(r => ids.Contains(Id(r["locator"]!)));
    }
    internal static JToken Resolve(JToken? locator, JObject before)
    {
        ConnectionCreatePlanner.Members(locator, "/locator", "project page type oid digest", "");
        Require(locator!["project"]?.Type == JTokenType.String && locator["page"]?.Type == JTokenType.String
            && locator["type"]?.Type == JTokenType.Integer && locator["oid"]?.Type == JTokenType.Integer
            && int.TryParse(locator["type"]!.ToString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int type) && type > 0
            && long.TryParse(locator["oid"]!.ToString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long oid) && oid > 0
            && locator["digest"]?.Type == JTokenType.String && ((string)locator["digest"]!).Length == 64
            && ((string)locator["digest"]!).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Use complete unmodified eplan_object_find locators.");
        JToken? found = before["objects"]!.SingleOrDefault(r => JToken.DeepEquals(r["locator"], locator));
        if (found is not null)
        { return found; }
        if (before["objects"]!.SelectMany(r => r["children"] as JArray ?? new JArray()).Any(r => JToken.DeepEquals(r["locator"], locator)))
        { throw Invalid("Property text is excluded; use independent Line Rectangle Text Arc Circle Ellipse PolyLine Spline or Group."); }
        throw Stale();
    }
    internal static double Number(JObject input, string field, double minimum, double maximum = double.MaxValue)
    {
        Require(input[field]?.Type is JTokenType.Integer or JTokenType.Float && (double)input[field]! >= minimum && (double)input[field]! <= maximum && !double.IsInfinity((double)input[field]!), field + " is required and must be finite within the allowed range.");
        return (double)input[field]!;
    }
    internal static double[] Point(JToken? value, int count = 2)
    {
        Require(value is JArray a && a.Count == count && a.All(n => n.Type is JTokenType.Integer or JTokenType.Float && Math.Abs((double)n) <= 10000), "Supply " + count + " finite mm coordinates in ±10000.");
        return value!.Values<double>().ToArray();
    }
    internal static string Id(JToken locator) => PlacementEditPlanner.Id(locator);
    internal static void Warn(JObject plan, string? warning) { if (!string.IsNullOrWhiteSpace(warning)) { plan["warning"] = string.Join("\n", new[] { (string?)plan["warning"], warning }.Where(s => !string.IsNullOrWhiteSpace(s))); } }
    internal static void Require(bool valid, string message) { if (!valid) { throw Invalid(message); } }
    internal static BridgeErrorException Invalid(string message) => WriteArguments.NotStarted(WriteArguments.Invalid([message]));
    internal static BridgeErrorException Stale() => WriteArguments.NotStarted(new(ErrorReason.PlanStale, "Object or page evidence changed; call eplan_object_find and preview again."));
}

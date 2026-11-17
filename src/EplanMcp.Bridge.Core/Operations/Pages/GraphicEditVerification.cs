using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

internal static class GraphicEditVerification
{
    internal static void Verify(JObject input, JObject before, JObject after, JObject plan)
    {
        string op = (string)input["operation"]!;
        JToken[] original = before["objects"]!.ToArray(), current = after["objects"]!.ToArray();
        var affected = new HashSet<string>(plan["affected"]!.Select(GraphicEditPlanner.Id), StringComparer.Ordinal);
        string Id(JToken row) => GraphicEditPlanner.Id(row["locator"]!);
        JToken? Find(JToken row) => current.SingleOrDefault(a => Id(a) == Id(row));
        foreach (JToken row in original.Where(o => !affected.Contains(Id(o))))
        { Check(Find(row) is { } found && Equal(Shape(row), Shape(found)), "An object outside the approved scope changed."); }
        Compare(before["functions"], after["functions"], plan, "Functions");
        Compare(before["connections"], after["connections"], plan, "Connections");
        foreach (string key in new[] { "pageSize", "gridSize" })
        { Compare(before["scene"]?["page"]?[key], after["scene"]?["page"]?[key], plan, "Page " + key); }
        JToken[] added = current.Where(a => !original.Any(b => Id(a) == Id(b))).ToArray();
        JToken[] actual = current.Where(a => affected.Contains(Id(a))).ToArray();
        JToken[] predicted = plan["predicted"]!.ToArray();
        foreach (JToken row in original.Where(o => affected.Contains(Id(o))))
        { if (Find(row) is { } observed) { GraphicEditReadback.Stable(row, observed, op, plan); } }
        if (op == "group")
        {
            Check(added.Length == 1 && (string?)added[0]["kind"] == "Group", "Group creation did not return exactly one group.");
            foreach (JToken row in predicted)
            { Check(Find(row) is { } a && a["group"] is JObject g && GraphicEditPlanner.Id(g) == Id(added[0]), "A group member is missing."); CompareGeometry(row, Find(row)!, plan); }
            Check(added[0]["members"] is JArray members && members.Select(GraphicEditPlanner.Id).OrderBy(s => s).SequenceEqual(predicted.Select(Id).OrderBy(s => s)), "The group membership differs.");
        }
        else if (op == "ungroup")
        {
            Check(added.Length == 0, "Ungroup unexpectedly created objects.");
            foreach (JToken row in predicted)
            {
                if ((string?)row["kind"] == "Group")
                { Check(Find(row) is null, "A group was not removed."); continue; }
                Check(Find(row) is { } a && a["group"] is null or { Type: JTokenType.Null }, "A member remains grouped.");
                CompareGeometry(row, Find(row)!, plan);
            }
        }
        else if (op == "explode_block")
        {
            Check(actual.Length == 0, "A block remains after explosion.");
            JToken[] members = predicted.SelectMany(p => p["memberStates"] ?? new JArray()).ToArray();
            Check(added.Length == members.Length, "The exploded member count differs.");
            var remaining = added.ToList();
            foreach (JToken expected in members)
            {
                JToken? match = remaining.FirstOrDefault(a => (expected["location"] is null or { Type: JTokenType.Null } || Equal(a["location"], expected["location"])) && (expected["bounds"] is null or { Type: JTokenType.Null } || Equal(a["bounds"], expected["bounds"])) && (string?)a["kind"] == (string?)expected["kind"]);
                Check(match is not null, "An exploded member geometry differs.");
                CompareGeometry(expected, match!, plan);
                remaining.Remove(match!);
            }
        }
        else if (op == "cut_off")
        {
            var remaining = actual.Concat(added).ToList();
            Check(remaining.All(r => (string?)r["kind"] == "Line"), "CutOff created a non-line object.");
            if ((bool?)plan["unverifiedCut"] != true)
            {
                Check(remaining.Count == predicted.Length, "The trimmed segment count differs.");
                foreach (JToken expected in predicted)
                {
                    JToken? match = remaining.FirstOrDefault(a => Equal(a["points"], expected["points"]));
                    Check(match is not null, "A trimmed segment differs.");
                    remaining.Remove(match!);
                }
            }
        }
        else
        {
            Check(actual.Length == predicted.Length, "An edited object disappeared.");
            foreach (JToken expected in predicted)
            {
                JToken found = Find(expected) ?? throw Mismatch("An edited object disappeared.");
                if (op is "scale" or "set_length" or "stretch" or "snap_to_grid" or "chamfer" or "fillet")
                { CompareGeometry(expected, found, plan); }
                if (op == "order" && (input["drawing_order"] is not null || (bool?)plan["orderKnown"] == true))
                { Compare(expected["drawingOrder"], found["drawingOrder"], plan, "drawingOrder"); }
                if (op == "order" && input["to"] is not null && found["drawingOrder"] is { Type: JTokenType.Integer or JTokenType.Float } n)
                {
                    double[] values = original.Select(r => (double?)r["drawingOrder"]).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                    Check(values.Length == 0 || ((string)input["to"]! == "front" ? (double)n >= values.Max() : (double)n <= values.Min()), "Native drawing order is not at the requested extreme.");
                }
                if (op is "dock_text" or "undock_text")
                { Compare(expected["docked"], found["docked"], plan, "docked"); if (input["dock_direction"] is not null && Id(expected) == GraphicEditPlanner.Id(input["locators"]![0]!)) { Compare(expected["dockingDirection"], found["dockingDirection"], plan, "dockingDirection"); } }
                if (op == "corner_radius")
                { Compare(expected["hasChamfers"], found["hasChamfers"], plan, "hasChamfers"); if ((bool?)expected["hasChamfers"] == true) { Compare(expected["cornerRadius"], found["cornerRadius"], plan, "cornerRadius"); } }
                if (op == "copy_format")
                { Compare(expected["pen"], found["pen"], plan, "pen"); Compare(expected["layer"], found["layer"], plan, "layer"); }
            }
            Check(added.Length == (plan["created"] as JArray)?.Count, "Unexpected created objects.");
            foreach ((JToken expected, JToken found) in (plan["created"] ?? new JArray()).Zip(added, (e, a) => (e, a)))
            { GraphicEditReadback.Created(expected, found, plan); }
        }
        plan["newLocators"] = new JArray(added.Select(a => a["locator"]!.DeepClone()));
        plan["actualRows"] = new JArray(actual.Concat(added).Select(a => a.DeepClone()));
    }
    private static void CompareGeometry(JToken expected, JToken actual, JObject plan)
    {
        // Live 2026-11-05: only a rectangle's bounds equal its geometry; lines and text add their pen and metrics, and a group's location follows them.
        string kind = (string?)expected["kind"] ?? "";
        foreach (string field in new[] { "location", "bounds", "points", "angle", "pen", "layer" })
        {
            if ((field != "bounds" || kind == "Rectangle") && (field != "location" || kind != "Group"))
            { Compare(expected[field], actual[field], plan, field); }
        }
    }
    internal static void Compare(JToken? expected, JToken? actual, JObject plan, string field)
    {
        if (expected is null or { Type: JTokenType.Null } || actual is null or { Type: JTokenType.Null })
        { GraphicEditPlanner.Warn(plan, field + " is unreadable; readback is unverified."); return; }
        Check(Equal(expected, actual), field + " does not match the approved edit.");
    }
    private static JObject Shape(JToken value)
    { var shape = (JObject)value.DeepClone(); foreach (string field in new[] { "locator", "drawing", "members" }) { shape.Remove(field); } return shape; }
    internal static bool Equal(JToken? x, JToken? y) => x?.Type is JTokenType.Integer or JTokenType.Float && y?.Type is JTokenType.Integer or JTokenType.Float
        ? Math.Abs((double)x - (double)y) < 1e-6 : x is JProperty xp && y is JProperty yp ? xp.Name == yp.Name && Equal(xp.Value, yp.Value)
        : x is JContainer xc && y is JContainer yc ? xc.Count == yc.Count && xc.Zip(yc, Equal).All(v => v) : JToken.DeepEquals(x, y);
    private static void Check(bool valid, string message) { if (!valid) { throw Mismatch(message); } }
    internal static BridgeErrorException Mismatch(string message) => new(ErrorReason.EplanOperationFailed, message + " Re-read the page.");
}

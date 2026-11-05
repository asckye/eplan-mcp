using System.Globalization;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

internal static class GraphicEditReadback
{
    internal static void Stable(JToken before, JToken after, string operation, JObject plan)
    {
        JObject Shape(JToken input)
        {
            var value = (JObject)input.DeepClone();
            foreach (string key in new[] { "locator", "drawing", "members" })
            { value.Remove(key); }
            var properties = value["properties"] as JObject;
            properties?.Remove("read_warning");
            if (operation is "scale" or "stretch" or "set_length" or "snap_to_grid" or "cut_off" or "chamfer" or "fillet")
            { foreach (string key in new[] { "location", "bounds", "points" }) { value.Remove(key); } properties?.Remove("points"); }
            if (operation == "scale")
            { foreach (string key in new[] { "height_mm", "height_raw_mm", "radius_mm", "radius_x_mm", "radius_y_mm", "start_angle", "end_angle", "arc_kind", "ellipse_angle" }) { properties?.Remove(key); } }
            if (operation is "group" or "ungroup")
            { value.Remove("group"); properties?.Remove("grouped"); }
            if (operation == "order")
            { value.Remove("drawingOrder"); }
            if (operation is "dock_text" or "undock_text")
            { value.Remove("docked"); value.Remove("dockingDirection"); properties?.Remove("docked"); }
            if (operation == "corner_radius")
            { value.Remove("hasChamfers"); value.Remove("cornerRadius"); }
            if (operation == "copy_format")
            {
                value.Remove("pen");
                value.Remove("layer");
                value.Remove("bounds");
                foreach (JProperty property in properties?.Properties().Where(p => p.Name == "layer" || p.Name.StartsWith("pen_", StringComparison.Ordinal)).ToArray() ?? [])
                { property.Remove(); }
            }
            return value;
        }
        CompareKnown(Shape(before), Shape(after), plan, "Unrequested placement data");
        if (operation == "scale" && (string?)before["kind"] == "Text")
        {
            JToken? expected = plan["predicted"]!.FirstOrDefault(r => GraphicEditPlanner.Id(r["locator"]!) == GraphicEditPlanner.Id(before["locator"]!));
            CompareNumber(expected?["properties"]?["height_mm"], after["properties"]?["height_mm"], plan, "Text height");
        }
    }
    internal static void Created(JToken expected, JToken actual, JObject plan)
    {
        GraphicEditVerification.Compare(expected["pen"], actual["pen"], plan, "created pen");
        GraphicEditVerification.Compare(expected["layer"], actual["layer"], plan, "created layer");
        GraphicEditVerification.Compare(expected["position"], actual["location"], plan, "created position");
        if ((string?)expected["kind"] == "line")
        { GraphicEditVerification.Compare(new JArray(expected["position"]!.DeepClone(), expected["endPosition"]!.DeepClone()), actual["points"], plan, "created line endpoints"); }
        else
        {
            foreach ((string input, string output) in new[] { ("radiusMm", "radius_mm"), ("startAngle", "start_angle"), ("endAngle", "end_angle") })
            { CompareNumber(expected[input], actual["properties"]?[output], plan, "created arc " + output); }
        }
    }
    private static void CompareNumber(JToken? expected, JToken? actual, JObject plan, string field)
    {
        bool Parse(JToken? value, out double number) => double.TryParse((string?)value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        if (Parse(expected, out double a) && Parse(actual, out double b))
        { if (Math.Abs(a - b) > 1e-6) { throw GraphicEditVerification.Mismatch(field + " differs."); } }
        else
        { GraphicEditPlanner.Warn(plan, field + " is unreadable; readback is unverified."); }
    }
    internal static void CompareKnown(JToken? expected, JToken? actual, JObject plan, string field)
    {
        if (expected is null or { Type: JTokenType.Null } || actual is null or { Type: JTokenType.Null })
        { if (!JToken.DeepEquals(expected, actual)) { GraphicEditPlanner.Warn(plan, field + " is unreadable; readback is unverified."); } return; }
        if (expected is JObject a && actual is JObject b)
        { foreach (string key in a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name))) { CompareKnown(a[key], b[key], plan, field + "/" + key); } return; }
        if (expected is JArray x && actual is JArray y && x.Count == y.Count)
        { for (int i = 0; i < x.Count; i++) { CompareKnown(x[i], y[i], plan, field); } return; }
        if (!GraphicEditVerification.Equal(expected, actual))
        { throw GraphicEditVerification.Mismatch(field + " changed."); }
    }
}

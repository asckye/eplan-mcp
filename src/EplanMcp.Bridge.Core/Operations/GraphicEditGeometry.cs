using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Detached endpoint geometry; arc angles follow PlacementText's counterclockwise degree convention.</summary>
public static class GraphicEditGeometry
{
    public static JObject Plan(JObject input, JArray selected)
    {
        var predicted = (JArray)selected.DeepClone();
        var result = new JObject { ["predicted"] = predicted, ["created"] = new JArray() };
        string operation = (string?)input["operation"] ?? "";
        Require(operation is "stretch" or "set_length" or "chamfer" or "fillet", "Use stretch, set_length, chamfer or fillet.");
        Require(predicted.Count > 0, "Select geometry to edit.");
        Require(operation is not ("chamfer" or "fillet") || predicted.Count == 2, "Select exactly two lines.");
        Require(predicted.All(p => (string?)p["kind"] == "Line" || operation == "stretch" && (string?)p["kind"] == "Rectangle"), "Supported kinds: Line; stretch also accepts Rectangle.");
        if (operation != "stretch")
        { Positive(input[operation == "set_length" ? "length_mm" : operation == "chamfer" ? "distance_mm" : "radius_mm"], operation == "set_length" ? "length_mm" : operation == "chamfer" ? "distance_mm" : "radius_mm"); }
        Require(operation != "set_length" || input["anchor"] is null || input["anchor"]!.Type == JTokenType.String && (string?)input["anchor"] is "start" or "end", "anchor must be start or end.");
        double[] offset = operation == "stretch" ? Vector(input["offset_mm"], 2, "offset_mm") : [];
        double[] region = operation == "stretch" ? Vector(input["region_mm"], 4, "region_mm") : [];
        if (region.Length > 0)
        { Require(region[0] <= region[2] && region[1] <= region[3], "region_mm must be [left,bottom,right,top]."); }
        if (predicted.Any(p => (string?)p["kind"] == "Line" ? p["points"] is not JArray { Count: 2 } ends || ends.Any(e => !Coordinates(e, 2)) : !Coordinates(p["bounds"], 4) || p["angle"]?.Type is not (JTokenType.Integer or JTokenType.Float) || !Finite((double)p["angle"]!)))
        { result["warning"] = "Geometry is unreadable; endpoint editing was not planned."; return result; }
        if (operation is "chamfer" or "fillet")
        { Corner(input, predicted, (JArray)result["created"]!); return result; }
        bool Inside(double[] point) => point[0] >= region[0] && point[0] <= region[2] && point[1] >= region[1] && point[1] <= region[3];
        foreach (JObject row in predicted)
        {
            if ((string?)row["kind"] == "Rectangle")
            {
                Require((double)row["angle"]! == 0, "Stretch supports only Rectangle with Angle 0.");
                double[] box = row["bounds"]!.Values<double>().ToArray();
                double[][] corners = [[box[0], box[1]], [box[2], box[1]], [box[2], box[3]], [box[0], box[3]]];
                int[] inside = Enumerable.Range(0, 4).Where(i => Inside(corners[i])).ToArray();
                Require(inside.Length > 0, "No rectangle corner lies in region_mm.");
                Require(inside.Length != 3 && !(inside.Length == 2 && (inside[1] - inside[0]) % 2 == 0), "Stretch rejects three corners or two opposite corners.");
                bool adjacent = inside.Length == 2;
                bool horizontal = adjacent && corners[inside[0]][1] == corners[inside[1]][1];
                foreach (int axis in Enumerable.Range(0, 2))
                {
                    if (adjacent && axis == (horizontal ? 0 : 1))
                    { if (offset[axis] != 0) { result["warning"] = "Stretch ignored the offset component along the selected rectangle edge."; } continue; }
                    foreach (double edge in inside.Select(i => corners[i][axis]).Distinct())
                    { int index = corners[0][axis] == edge ? axis : axis + 2; box[index] += offset[axis]; }
                }
                Require(box[0] != box[2] && box[1] != box[3], "Stretch must leave a nonzero rectangle area.");
                row["bounds"] = new JArray(Math.Min(box[0], box[2]), Math.Min(box[1], box[3]), Math.Max(box[0], box[2]), Math.Max(box[1], box[3]));
                row["location"] = new JArray(row["bounds"]![0]!.DeepClone(), row["bounds"]![1]!.DeepClone());
                continue;
            }
            double[][] points = Points(row);
            if (operation == "set_length")
            {
                double length = Positive(input["length_mm"], "length_mm");
                string anchor = (string?)input["anchor"] ?? "start";
                Require(anchor is "start" or "end", "anchor must be start or end.");
                double[] delta = Subtract(points[1], points[0]);
                Require(Norm(delta) > 0, "A zero-length line has no direction; select a nonzero Line.");
                int moving = anchor == "start" ? 1 : 0;
                points[moving] = Add(points[1 - moving], Multiply(delta, length / Norm(delta) * (moving == 1 ? 1 : -1)));
            }
            else
            {
                Require(points.Any(Inside), "No line endpoint lies in region_mm.");
                points = points.Select(p => Inside(p) ? Add(p, offset) : p).ToArray();
                Require(Norm(Subtract(points[1], points[0])) > 0, "Stretch must leave a nonzero line length.");
            }
            SetLine(row, points);
        }
        return result;
    }

    private static void Corner(JObject input, JArray rows, JArray created)
    {
        double[][] first = Points(rows[0]!), second = Points(rows[1]!);
        var matches = (from i in Enumerable.Range(0, 2) from j in Enumerable.Range(0, 2) where Norm(Subtract(first[i], second[j])) <= 0.01 select (i, j)).ToArray();
        Require(matches.Length == 1, "Two lines must have exactly one coincident endpoint pair within 0.01 mm.");
        (int a, int b) = matches[0];
        double[] u = Subtract(first[1 - a], first[a]), v = Subtract(second[1 - b], second[b]);
        double firstLength = Norm(u), secondLength = Norm(v);
        Require(firstLength > 0 && secondLength > 0, "Select two nonzero lines.");
        u = Multiply(u, 1 / firstLength);
        v = Multiply(v, 1 / secondLength);
        double cross = Cross(u, v);
        Require(Math.Abs(cross) > 1e-10, "Parallel lines cannot be chamfered or filleted.");
        double[] vertex = Add(first[a], Multiply(u, Cross(Subtract(second[b], first[a]), v) / cross));
        double angle = Math.Acos(Math.Max(-1, Math.Min(1, Dot(u, v))));
        bool fillet = (string?)input["operation"] == "fillet";
        double size = Positive(input[fillet ? "radius_mm" : "distance_mm"], fillet ? "radius_mm" : "distance_mm");
        double distance = fillet ? size / Math.Tan(angle / 2) : size;
        Require(size <= firstLength + 1e-9 && size <= secondLength + 1e-9 && distance <= Dot(Subtract(first[1 - a], vertex), u) + 1e-9 && distance <= Dot(Subtract(second[1 - b], vertex), v) + 1e-9, "Requested distance or radius exceeds the lines' available length.");
        first[a] = Add(vertex, Multiply(u, distance));
        second[b] = Add(vertex, Multiply(v, distance));
        SetLine((JObject)rows[0]!, first);
        SetLine((JObject)rows[1]!, second);
        var item = new JObject { ["kind"] = fillet ? "arc" : "line", ["position"] = new JArray(first[a]), ["endPosition"] = new JArray(second[b]) };
        if (fillet)
        {
            double[] bisector = Add(u, v), center = Add(vertex, Multiply(bisector, size / Math.Sin(angle / 2) / Norm(bisector)));
            double start = Degrees(Subtract(first[a], center)), end = Degrees(Subtract(second[b], center));
            item["position"] = new JArray(center);
            item.Remove("endPosition");
            item["radiusMm"] = size;
            item["startAngle"] = cross > 0 ? end : start;
            item["endAngle"] = cross > 0 ? start : end;
        }
        foreach (string style in new[] { "pen", "layer" })
        { if (rows[0]![style] is { } value) { item[style] = value.DeepClone(); } }
        created.Add(item);
    }
    private static void SetLine(JObject row, double[][] points)
    {
        row["points"] = new JArray(points.Select(p => new JArray(p)));
        row["location"] = new JArray(points[0]);
        row["bounds"] = new JArray(points.Min(p => p[0]), points.Min(p => p[1]), points.Max(p => p[0]), points.Max(p => p[1]));
    }
    private static double[][] Points(JToken row) => row["points"]!.Select(p => p.Values<double>().ToArray()).ToArray();
    private static bool Coordinates(JToken? token, int count) => token is JArray array && array.Count == count && array.All(n => n.Type is JTokenType.Integer or JTokenType.Float && Finite((double)n));
    private static double[] Vector(JToken? value, int count, string name) { Require(Coordinates(value, count), name + " requires finite numeric coordinates."); return value!.Values<double>().ToArray(); }
    private static double Positive(JToken? value, string name) { Require(value?.Type is JTokenType.Integer or JTokenType.Float && Finite((double)value) && (double)value > 0, name + " must be finite and positive."); return (double)value!; }
    private static double[] Add(double[] a, double[] b) => [a[0] + b[0], a[1] + b[1]];
    private static double[] Subtract(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1]];
    private static double[] Multiply(double[] a, double value) => [a[0] * value, a[1] * value];
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1];
    private static double Cross(double[] a, double[] b) => a[0] * b[1] - a[1] * b[0];
    private static double Norm(double[] value) => Math.Sqrt(Dot(value, value));
    private static double Degrees(double[] value) => (Math.Atan2(value[1], value[0]) * 180 / Math.PI + 360) % 360;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static void Require(bool condition, string message) { if (!condition) { throw WriteArguments.NotStarted(WriteArguments.Invalid([message])); } }
}

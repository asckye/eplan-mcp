using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

internal static class GraphicEditPredictions
{
    internal static JObject Plan(JObject input, JObject before, JArray selected)
    {
        string op = (string)input["operation"]!;
        if (op is "set_length" or "stretch" or "chamfer" or "fillet")
        { return GraphicEditGeometry.Plan(input, selected); }
        if (op == "cut_off")
        { return GraphicEditCutOff.Plan(input, before, selected); }
        var plan = new JObject { ["predicted"] = selected.DeepClone(), ["created"] = new JArray(), ["noOp"] = false };
        var predicted = (JArray)plan["predicted"]!;
        if (op == "scale")
        {
            double sx = GraphicEditPlanner.Number(input, "scale_x", .01, 100), sy = GraphicEditPlanner.Number(input, "scale_y", .01, 100);
            double[] origin = GraphicEditPlanner.Point(input["origin_mm"]);
            predicted = new JArray(GraphicEditPlanner.Closure(selected, before, op).Select(r => r.DeepClone()));
            plan["predicted"] = predicted;
            foreach (JObject row in predicted)
            {
                double[] Transform(double[] p) => [origin[0] + (p[0] - origin[0]) * sx, origin[1] + (p[1] - origin[1]) * sy];
                TransformRow(row, Transform, plan);
                if (double.TryParse((string?)row["properties"]?["height_mm"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double height) && height > 0)
                { row["properties"]!["height_mm"] = (height * sy).ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
            }
            plan["noOp"] = sx == 1 && sy == 1;
        }
        else if (op == "order")
        {
            GraphicEditPlanner.Require((input["to"] is null) != (input["drawing_order"] is null), "Choose to or drawing_order, not both.");
            GraphicEditPlanner.Require(input["to"] is null || input["to"]!.Type == JTokenType.String && (string?)input["to"] is "front" or "back", "to must be front or back.");
            if (input["drawing_order"] is not null)
            {
                GraphicEditPlanner.Require(input["drawing_order"]!.Type == JTokenType.Integer, "drawing_order must be an integer.");
                double order = GraphicEditPlanner.Number(input, "drawing_order", 0, 127);
                foreach (JObject row in predicted)
                { row["drawingOrder"] = order; }
                plan["noOp"] = selected.All(r => (double?)r["drawingOrder"] == order);
            }
            else
            {
                plan["orderTo"] = input["to"]!.DeepClone();
                JToken[] objects = before["objects"]!.ToArray();
                if (objects.All(r => r["drawingOrder"]?.Type is JTokenType.Integer or JTokenType.Float))
                {
                    var orders = objects.ToDictionary(r => GraphicEditPlanner.Id(r["locator"]!), r => (double)r["drawingOrder"]!);
                    bool front = (string?)input["to"] == "front";
                    plan["orderKnown"] = true;
                    foreach (JObject row in predicted)
                    {
                        string id = GraphicEditPlanner.Id(row["locator"]!);
                        double edge = front ? orders.Values.Max() : orders.Values.Min();
                        if (orders[id] != edge || orders.Values.Count(n => n == edge) > 1)
                        {
                            if (edge == (front ? 127 : 0))
                            { plan["orderKnown"] = false; GraphicEditPlanner.Warn(plan, "Native drawing-order boundary handling is unverified."); break; }
                            orders[id] = edge + (front ? 1 : -1);
                        }
                        row["drawingOrder"] = orders[id];
                    }
                }
                else
                { GraphicEditPlanner.Warn(plan, "Drawing order is unreadable; the native extreme is checked after editing."); }
            }
            if (selected.Any(r => r["drawingOrder"] is null or { Type: JTokenType.Null }))
            { GraphicEditPlanner.Warn(plan, "Drawing order is unreadable; order prediction is unverified."); }
        }
        else if (op == "dock_text")
        {
            GraphicEditPlanner.Require(input["dock_direction"] is null || input["dock_direction"]!.Type == JTokenType.String && (string?)input["dock_direction"] is "bottom" or "top" or "right" or "left", "dock_direction must be bottom, top, right or left.");
            predicted[0]!["docked"] = true;
            if (input["dock_direction"] is not null)
            { predicted[0]!["dockingDirection"] = "DockTo" + char.ToUpperInvariant(((string)input["dock_direction"]!)[0]) + ((string)input["dock_direction"]!).Substring(1); }
        }
        else if (op == "undock_text")
        {
            foreach (JObject row in predicted)
            { row["docked"] = false; }
            plan["noOp"] = selected.All(r => (bool?)r["docked"] == false);
        }
        else if (op == "corner_radius")
        {
            double radius = GraphicEditPlanner.Number(input, "radius_mm", 0);
            foreach (JObject row in predicted)
            { row["hasChamfers"] = radius > 0; if (radius > 0) { row["cornerRadius"] = radius; } }
        }
        else if (op == "copy_format")
        {
            JToken source = GraphicEditPlanner.Resolve(input["source"], before);
            foreach (JObject row in predicted)
            {
                foreach (string field in new[] { "pen", "layer" })
                {
                    row[field] = source[field]?.DeepClone();
                    if (source[field] is null or { Type: JTokenType.Null })
                    { GraphicEditPlanner.Warn(plan, "Source " + field + " is unreadable; its readback is unverified."); }
                }
            }
        }
        else if (op == "snap_to_grid")
        {
            double grid = (double?)before["scene"]?["page"]?["gridSize"] ?? 0;
            if (grid <= 0 || double.IsInfinity(grid) || double.IsNaN(grid))
            { GraphicEditPlanner.Warn(plan, "Page grid is unreadable; no snap can be calculated."); plan["noOp"] = true; return plan; }
            predicted = new JArray(GraphicEditPlanner.Closure(selected, before, op).Select(r => r.DeepClone()));
            plan["predicted"] = predicted;
            var skipped = new JArray();
            plan["skipped"] = skipped;
            foreach (JObject root in selected)
            {
                if ((string?)root["kind"] == "Line" && (root["points"] is not JArray { Count: 2 } ends || ends.Any(p => !Coordinates(p, 2))))
                { GraphicEditPlanner.Warn(plan, "Line endpoints are unreadable; snap skipped for this placement."); skipped.Add(root["locator"]!.DeepClone()); continue; }
                if (!Coordinates(root["location"], 2) || root["location"]!.Values<double>().Any(v => Math.Abs(v) > 10000))
                { GraphicEditPlanner.Warn(plan, "Location is unreadable; snap skipped for this placement."); skipped.Add(root["locator"]!.DeepClone()); continue; }
                double[] delta = root["location"]!.Values<double>().Select(v => Math.Sign(v) * DrawingCoordinates.Snap(Math.Abs(v), grid) - v).ToArray();
                if (delta.All(d => d == 0))
                { continue; }
                var ids = new HashSet<string>(GraphicEditPlanner.Closure(new JArray(root.DeepClone()), before, op).Select(r => GraphicEditPlanner.Id(r["locator"]!)), StringComparer.Ordinal);
                foreach (JObject row in predicted.Where(r => ids.Contains(GraphicEditPlanner.Id(r["locator"]!))))
                { TransformRow(row, p => p.Zip(delta, (v, d) => v + d).ToArray(), plan); }
            }
            plan["noOp"] = JToken.DeepEquals(predicted, new JArray(GraphicEditPlanner.Closure(selected, before, op).Select(r => r.DeepClone())));
        }
        else if (op is "group" or "ungroup" or "explode_block")
        {
            plan["predicted"] = new JArray(GraphicEditPlanner.Closure(selected, before, op).Select(r => r.DeepClone()));
        }
        return plan;
    }
    private static void TransformRow(JObject row, Func<double[], double[]> transform, JObject plan)
    {
        if (Coordinates(row["location"], 2))
        { row["location"] = new JArray(transform(row["location"]!.Values<double>().ToArray())); }
        else
        { GraphicEditPlanner.Warn(plan, "Placement location is unreadable; position prediction is unverified."); }
        if (row["points"] is JArray points && points.All(p => Coordinates(p, 2)))
        { row["points"] = new JArray(points.Select(point => new JArray(transform(point.Values<double>().ToArray())))); }
        else if (row["points"] is not (null or { Type: JTokenType.Null }))
        { GraphicEditPlanner.Warn(plan, "Placement points are unreadable; point prediction is unverified."); }
        if (Coordinates(row["bounds"], 4))
        {
            JToken b = row["bounds"]!;
            double[] low = transform([(double)b[0]!, (double)b[1]!]), high = transform([(double)b[2]!, (double)b[3]!]);
            row["bounds"] = new JArray(low[0], low[1], high[0], high[1]);
        }
        else
        { GraphicEditPlanner.Warn(plan, "Placement bounds are unreadable; boundary prediction is unverified."); }
    }
    internal static bool Coordinates(JToken? value, int count) => value is JArray array && array.Count == count && array.All(n => n.Type is JTokenType.Integer or JTokenType.Float && !double.IsNaN((double)n) && !double.IsInfinity((double)n));
}

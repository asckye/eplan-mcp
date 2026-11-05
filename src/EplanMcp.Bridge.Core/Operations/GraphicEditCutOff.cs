using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

internal static class GraphicEditCutOff
{
    internal static JObject Plan(JObject input, JObject before, JArray selected)
    {
        double[] point = GraphicEditPlanner.Point(input["point_mm"]);
        var result = new JObject { ["predicted"] = selected.DeepClone(), ["created"] = new JArray(), ["noOp"] = false };
        if (selected[0]!["points"] is not JArray { Count: 2 } ends || ends.Any(p => !GraphicEditPredictions.Coordinates(p, 2)))
        { GraphicEditPlanner.Warn(result, "Line endpoints are unreadable; cut_off cannot be calculated."); result["noOp"] = true; return result; }
        double[] a = ends[0]!.Values<double>().ToArray(), b = ends[1]!.Values<double>().ToArray();
        GraphicEditPlanner.Require(Distance(point, a, b) <= .5, "point_mm must be within 0.5 mm of the selected line.");
        var crossings = new List<double> { 0, 1 };
        foreach (JToken other in before["objects"]!.Where(o => GraphicEditPlanner.Id(o["locator"]!) != GraphicEditPlanner.Id(selected[0]!["locator"]!) && o["group"] is null or { Type: JTokenType.Null }))
        {
            if ((bool?)other["graphical"] == false)
            { continue; }
            if (CurveDistance(other, point) is { } curveDistance)
            { GraphicEditPlanner.Require(curveDistance > .5, "Another independent graphic is within 0.5 mm of point_mm; choose an unambiguous cut point."); }
            double[][][] segments = Segments(other).ToArray();
            if (segments.Length == 0)
            { GraphicEditPlanner.Warn(result, "Graphic segment geometry is unreadable; cut proximity and intersections are unverified."); result["unverifiedCut"] = true; continue; }
            GraphicEditPlanner.Require(!segments.Any(s => Distance(point, s[0], s[1]) <= .5), "Another independent graphic is within 0.5 mm of point_mm; choose an unambiguous cut point.");
            foreach (double[][] segment in segments)
            {
                double? t = Intersection(a, b, segment[0], segment[1]);
                if (t is > 0 and < 1)
                { crossings.Add(t.Value); }
            }
        }
        double length2 = (b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]);
        GraphicEditPlanner.Require(length2 > 0, "cut_off requires a nonzero line.");
        double position = Math.Max(0, Math.Min(1, ((point[0] - a[0]) * (b[0] - a[0]) + (point[1] - a[1]) * (b[1] - a[1])) / length2));
        double low = crossings.Where(t => t < position).DefaultIfEmpty(0).Max(), high = crossings.Where(t => t > position).DefaultIfEmpty(1).Min();
        double[] At(double t) => [a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1])];
        var kept = new JArray();
        if (low > 0)
        { kept.Add(new JArray(new JArray(a), new JArray(At(low)))); }
        if (high < 1)
        { kept.Add(new JArray(new JArray(At(high)), new JArray(b))); }
        result["segments"] = kept;
        result["predicted"] = new JArray(kept.Select(s =>
        {
            var row = (JObject)selected[0]!.DeepClone();
            row["points"] = s.DeepClone();
            row["location"] = s[0]!.DeepClone();
            row["bounds"] = new JArray(Math.Min((double)s[0]![0]!, (double)s[1]![0]!), Math.Min((double)s[0]![1]!, (double)s[1]![1]!), Math.Max((double)s[0]![0]!, (double)s[1]![0]!), Math.Max((double)s[0]![1]!, (double)s[1]![1]!));
            return row;
        }));
        if (kept.Count == 0)
        { GraphicEditPlanner.Warn(result, "The selected line will be removed completely (last segment)."); }
        return result;
    }
    internal static double Distance(double[] p, double[] a, double[] b)
    {
        double dx = b[0] - a[0], dy = b[1] - a[1], d = dx * dx + dy * dy;
        double t = d == 0 ? 0 : Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / d));
        return Math.Sqrt(Math.Pow(p[0] - a[0] - t * dx, 2) + Math.Pow(p[1] - a[1] - t * dy, 2));
    }
    private static double? Intersection(double[] a, double[] b, double[] c, double[] d)
    {
        double dx = b[0] - a[0], dy = b[1] - a[1], ex = d[0] - c[0], ey = d[1] - c[1], cross = dx * ey - dy * ex;
        if (Math.Abs(cross) < 1e-10)
        { return null; }
        double t = ((c[0] - a[0]) * ey - (c[1] - a[1]) * ex) / cross, u = ((c[0] - a[0]) * dy - (c[1] - a[1]) * dx) / cross;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? t : null;
    }
    private static IEnumerable<double[][]> Segments(JToken row)
    {
        if ((string?)row["kind"] is "Line" or "PolyLine" && row["points"] is JArray points && points.All(p => GraphicEditPredictions.Coordinates(p, 2)))
        {
            for (int i = 1; i < points.Count; i++)
            { yield return [points[i - 1]!.Values<double>().ToArray(), points[i]!.Values<double>().ToArray()]; }
            if (points.Count > 2 && (string?)row["properties"]?["closed"] == "true")
            { yield return [points[points.Count - 1]!.Values<double>().ToArray(), points[0]!.Values<double>().ToArray()]; }
        }
        else if ((string?)row["kind"] == "Rectangle" && GraphicEditPredictions.Coordinates(row["bounds"], 4) && (double?)row["angle"] == 0)
        {
            JToken b = row["bounds"]!;
            double[][] corners = [[(double)b[0]!, (double)b[1]!], [(double)b[2]!, (double)b[1]!], [(double)b[2]!, (double)b[3]!], [(double)b[0]!, (double)b[3]!]];
            for (int i = 0; i < 4; i++)
            { yield return [corners[i], corners[(i + 1) % 4]]; }
        }
    }
    private static double? CurveDistance(JToken row, double[] point)
    {
        if ((string?)row["kind"] is not ("Arc" or "Circle" or "Ellipse") || !GraphicEditPredictions.Coordinates(row["location"], 2))
        { return null; }
        bool Read(string name, out double value) => double.TryParse((string?)row["properties"]?[name], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        double x = point[0] - (double)row["location"]![0]!, y = point[1] - (double)row["location"]![1]!;
        string? kind = (string?)row["properties"]?["arc_kind"] ?? (string?)row["kind"];
        if (kind == "Ellipse")
        {
            if (!Read("radius_x_mm", out double rx) || !Read("radius_y_mm", out double ry) || !Read("ellipse_angle", out double angle) || rx <= 0 || ry <= 0)
            { return null; }
            return EllipseDistance(Math.Abs(x * Math.Cos(angle) + y * Math.Sin(angle)), Math.Abs(y * Math.Cos(angle) - x * Math.Sin(angle)), rx, ry);
        }
        if (!Read("radius_mm", out double radius) || radius <= 0)
        { return null; }
        double radial = Math.Abs(Math.Sqrt(x * x + y * y) - radius);
        if (kind == "Circle")
        { return radial; }
        if (!Read("start_angle", out double start) || !Read("end_angle", out double end))
        { return null; }
        double Normalize(double a) => (a % 360 + 360) % 360;
        double sweep = Normalize(end - start), direction = Normalize(Math.Atan2(y, x) * 180 / Math.PI - start);
        if (sweep == 0 || direction <= sweep)
        { return radial; }
        double Endpoint(double angle) => Math.Sqrt(Math.Pow(x - radius * Math.Cos(angle * Math.PI / 180), 2) + Math.Pow(y - radius * Math.Sin(angle * Math.PI / 180), 2));
        return Math.Min(Endpoint(start), Endpoint(end));
    }
    private static double EllipseDistance(double x, double y, double rx, double ry)
    {
        // Solve the closest-point Lagrange multiplier in the first quadrant with the major axis first.
        if (rx < ry)
        { (rx, ry) = (ry, rx); (x, y) = (y, x); }
        if (y == 0)
        {
            double numerator = rx * x, denominator = rx * rx - ry * ry;
            double px = numerator < denominator ? rx * numerator / denominator : rx;
            double py = numerator < denominator ? ry * Math.Sqrt(Math.Max(0, 1 - px * px / (rx * rx))) : 0;
            return Math.Sqrt((px - x) * (px - x) + py * py);
        }
        if (x == 0)
        { return Math.Abs(y - ry); }
        double z0 = x / rx, z1 = y / ry, ratio = rx * rx / (ry * ry), low = z1 - 1;
        double high = z0 * z0 + z1 * z1 < 1 ? 0 : Math.Sqrt(ratio * ratio * z0 * z0 + z1 * z1) - 1;
        for (int i = 0; i < 80; i++)
        {
            double mid = (low + high) / 2, a = ratio * z0 / (mid + ratio), b = z1 / (mid + 1);
            if (a * a + b * b > 1)
            { low = mid; }
            else
            { high = mid; }
        }
        double t = (low + high) / 2, nearestX = ratio * x / (t + ratio), nearestY = y / (t + 1);
        return Math.Sqrt((nearestX - x) * (nearestX - x) + (nearestY - y) * (nearestY - y));
    }
}

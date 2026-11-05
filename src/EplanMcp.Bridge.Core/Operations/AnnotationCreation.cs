using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Annotation-specific constraints inside the shared primitive transaction.</summary>
public static class AnnotationCreation
{
    public static bool Known(string kind) => kind is "hyperlink" or "qr_code" or "spline" or "sector";

    internal static void Parse(JToken row, string at)
    {
        foreach (string field in new[] { "link", "text", "dimensionType", "name" })
        { if (row[field] is { Type: not JTokenType.Null and not JTokenType.String }) { throw WriteArguments.Invalid([at + "/" + field + " must be a string."]); } }
        foreach (string field in new[] { "records", "variables" })
        { if (row[field] is { Type: not JTokenType.Null } value && (value is not JArray a || a.Any(v => v.Type != JTokenType.String))) { throw WriteArguments.Invalid([at + "/" + field + " requires strings."]); } }
        string? kind = (string?)row["kind"];
        bool Number(JToken? n) => n?.Type is JTokenType.Integer or JTokenType.Float && !double.IsNaN((double)n) && !double.IsInfinity((double)n);
        bool Point(JToken? p) => p is JArray { Count: 2 } xy && xy.All(Number);
        if ((kind == "spline") && (row["points"] is not JArray points || points.Count < 2 || !points.All(Point)))
        { throw WriteArguments.Invalid([at + "/points requires finite coordinate pairs."]); }
        if (row["dimensionText"] is { Type: not JTokenType.Null })
        { throw WriteArguments.Invalid([at + "/dimensionText is computed by EPLAN."]); }
        if (row["sizeMm"] is { Type: not (JTokenType.Float or JTokenType.Integer) })
        { throw WriteArguments.Invalid([at + "/sizeMm must be a number."]); }
    }

    internal static void Validate(PlacementText item)
    {
        bool Point(double[] p) => p is { Length: 2 } && p.All(n => !double.IsNaN(n) && !double.IsInfinity(n) && n is >= 0 and <= 10000);
        bool Nonempty(string? s) => !string.IsNullOrWhiteSpace(s);
        bool valid = item.Kind switch
        {
            "hyperlink" => Nonempty(item.Link) && item.Contents.Count > 0,
            "qr_code" => Nonempty(item.Text) && item.SizeMm is > 0 and <= 10000,
            "spline" => item.Points.Length >= 2 && item.Points.All(Point),
            "sector" => item.RadiusMm is > 0 and <= 10000 && !double.IsNaN(item.StartAngle) && !double.IsInfinity(item.StartAngle) && !double.IsNaN(item.EndAngle) && !double.IsInfinity(item.EndAngle),
            _ => true
        };
        if (!valid)
        { throw WriteArguments.Invalid(["/items: invalid annotation fields or coincident angular points."]); }
    }

    internal static bool Matches(PlacementText expected, PlacementText actual)
    {
        bool Strings(string[]? a, string[]? b) => a is null || b is not null && a.SequenceEqual(b);
        return expected.Link == actual.Link && expected.Text == actual.Text && expected.Name == actual.Name
            && Math.Abs(expected.SizeMm - actual.SizeMm) < 1e-6 && Strings(expected.Records, actual.Records) && Strings(expected.Variables, actual.Variables);
    }

    internal static double[]? Bounds(PlacementText p)
    {
        if (p.Kind is "hyperlink")
        { return null; }
        if (p.Kind == "qr_code")
        { return [p.Position[0], p.Position[1], p.Position[0] + p.SizeMm, p.Position[1] + p.SizeMm]; }
        if (p.Kind == "sector")
        { return [p.Position[0] - p.RadiusMm, p.Position[1] - p.RadiusMm, p.Position[0] + p.RadiusMm, p.Position[1] + p.RadiusMm]; }
        return [p.Points.Min(q => q[0]), p.Points.Min(q => q[1]), p.Points.Max(q => q[0]), p.Points.Max(q => q[1])];
    }

    internal static string NativeKind(string kind) => kind switch
    { "hyperlink" => "HyperLink", "qr_code" => "QRCode", "spline" => "Spline", "sector" => "Arc", _ => "DimensionItem" };
}

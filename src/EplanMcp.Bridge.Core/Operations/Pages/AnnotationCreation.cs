using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Annotation-specific constraints inside the shared primitive transaction.</summary>
public static class AnnotationCreation
{
    public static bool Known(string kind) => kind is "hyperlink" or "qr_code" or "spline" or "sector" or "dimension" or "placeholder" or "macro_box";

    internal static void Parse(JToken row, string at)
    {
        foreach (string field in new[] { "link", "text", "dimensionType", "name" })
        { if (row[field] is { Type: not JTokenType.Null and not JTokenType.String }) { throw WriteArguments.Invalid([at + "/" + field + " must be a string."]); } }
        foreach (string field in new[] { "records", "variables" })
        { if (row[field] is { Type: not JTokenType.Null } value && (value is not JArray a || a.Any(v => v.Type != JTokenType.String))) { throw WriteArguments.Invalid([at + "/" + field + " requires strings."]); } }
        string? kind = (string?)row["kind"];
        bool Number(JToken? n) => n?.Type is JTokenType.Integer or JTokenType.Float && !double.IsNaN((double)n) && !double.IsInfinity((double)n);
        bool Point(JToken? p) => p is JArray { Count: 2 } xy && xy.All(Number);
        if ((kind == "spline" || kind == "dimension" && row["dimensionType"]?.ToString() != "circle") && (row["points"] is not JArray points || points.Count < 2 || !points.All(Point)))
        { throw WriteArguments.Invalid([at + "/points requires finite coordinate pairs."]); }
        if (kind == "dimension" && !Point(row["endPosition"]))
        { throw WriteArguments.Invalid([at + "/endPosition requires a finite coordinate pair."]); }
        if (kind == "dimension" && row["dimensionType"]?.ToString() == "circle")
        {
            if (row["arc"] is not JObject arc || arc.Properties().Count() != 5 || arc["project"]?.Type != JTokenType.String || arc["page"]?.Type != JTokenType.String
                || arc["type"]?.Type != JTokenType.Integer || arc["oid"]?.Type != JTokenType.Integer || arc["digest"]?.Type != JTokenType.String)
            { throw WriteArguments.Invalid([at + "/arc requires a complete uncoerced locator."]); }
        }
        if (row["dimensionText"] is { Type: not JTokenType.Null })
        { throw WriteArguments.Invalid([at + "/dimensionText is computed by EPLAN."]); }
        if (row["sizeMm"] is { Type: not (JTokenType.Float or JTokenType.Integer) })
        { throw WriteArguments.Invalid([at + "/sizeMm must be a number."]); }
    }

    internal static void Validate(PlacementText item)
    {
        bool Point(double[] p) => p is { Length: 2 } && p.All(n => !double.IsNaN(n) && !double.IsInfinity(n) && n is >= 0 and <= 10000);
        bool Nonempty(string? s) => !string.IsNullOrWhiteSpace(s);
        if (item.Kind is "dimension" or "placeholder" or "macro_box" && (item.Layer is not null || item.Pen is not null))
        { throw WriteArguments.Invalid(["/items: style fields apply to hyperlink, qr_code, spline and sector."]); }
        bool valid = item.Kind switch
        {
            "hyperlink" => Nonempty(item.Link) && item.Contents.Count > 0,
            "qr_code" => Nonempty(item.Text) && item.SizeMm is > 0 and <= 10000,
            "spline" => item.Points.Length >= 2 && item.Points.All(Point),
            "sector" => item.RadiusMm is > 0 and <= 10000 && !double.IsNaN(item.StartAngle) && !double.IsInfinity(item.StartAngle) && !double.IsNaN(item.EndAngle) && !double.IsInfinity(item.EndAngle),
            "placeholder" => Nonempty(item.Name) && (item.Records ?? []).All(Nonempty) && (item.Variables ?? []).All(Nonempty),
            "macro_box" => Nonempty(item.Name),
            "dimension" => Point(item.EndPosition) && (item.DimensionType switch
            {
                "circle" => item.Arc is { Type: > 0, Oid: > 0 } arc && Nonempty(arc.Project) && Nonempty(arc.Page) && arc.Digest is { Length: 64 },
                "linear" => item.Points.Length == 2 && item.Points.All(Point),
                "angular" => item.Points.Length == 2 && item.Points.All(Point) && !item.Points[0].SequenceEqual(item.Points[1]) && item.Points.All(p => !p.SequenceEqual(item.EndPosition)),
                "baseline" or "incremental" or "continued" => item.Points.Length >= 2 && item.Points.All(Point),
                _ => false
            }),
            _ => true
        };
        if (!valid)
        { throw WriteArguments.Invalid(["/items: invalid annotation fields or coincident angular points."]); }
    }

    internal static bool Matches(PlacementText expected, PlacementText actual)
    {
        bool Strings(string[]? a, string[]? b) => a is null || b is not null && a.SequenceEqual(b);
        if (expected.Kind == "dimension")
        {
            int count = expected.DimensionType is "baseline" or "incremental" or "continued" ? expected.Points.Length - 1 : 1;
            return actual.Position.Length == 2 && actual.Position.All(n => !double.IsNaN(n) && !double.IsInfinity(n))
                && (expected.DimensionType is not ("linear" or "angular") || actual.EndPosition.Length == 2 && actual.EndPosition.All(n => !double.IsNaN(n) && !double.IsInfinity(n)))
                && actual.DimensionType == expected.DimensionType && actual.DimensionText?.Length == count && actual.DimensionText.All(s => !string.IsNullOrWhiteSpace(s));
        }
        return expected.Link == actual.Link && expected.Text == actual.Text && expected.Name == actual.Name
            && Math.Abs(expected.SizeMm - actual.SizeMm) < 1e-6 && Strings(expected.Records, actual.Records) && Strings(expected.Variables, actual.Variables);
    }

    internal static double[]? Bounds(PlacementText p)
    {
        if (p.Kind is "hyperlink" or "placeholder" or "macro_box" or "dimension")
        { return null; }
        if (p.Kind == "qr_code")
        { return [p.Position[0], p.Position[1], p.Position[0] + p.SizeMm, p.Position[1] + p.SizeMm]; }
        if (p.Kind == "sector")
        { return [p.Position[0] - p.RadiusMm, p.Position[1] - p.RadiusMm, p.Position[0] + p.RadiusMm, p.Position[1] + p.RadiusMm]; }
        return [p.Points.Min(q => q[0]), p.Points.Min(q => q[1]), p.Points.Max(q => q[0]), p.Points.Max(q => q[1])];
    }

    internal static string NativeKind(string kind) => kind switch
    { "hyperlink" => "HyperLink", "qr_code" => "QRCode", "spline" => "Spline", "sector" => "Arc", "placeholder" => "PlaceHolder", "macro_box" => "MacroBox", _ => "DimensionItem" };
}

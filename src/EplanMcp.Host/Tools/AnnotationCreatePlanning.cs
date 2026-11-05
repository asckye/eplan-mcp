using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

internal static class AnnotationCreatePlanning
{
    internal static readonly string[] Kinds = ["hyperlink", "qr_code", "spline", "sector"];
    internal static readonly string[] DimensionTypes = ["linear", "angular", "baseline", "incremental", "continued", "circle"];
    private static readonly string[] _textFields = ["link", "text", "name", "macro_name"];
    private static readonly string[] _listFields = ["records", "variables"];
    private static readonly string[] _geometryFields = ["pen", "layer", "position", "center", "radius_mm", "start_angle", "end_angle", "points"];
    private static readonly string[] _dimensionPoints = ["first", "second", "end"];
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static void Validate(JsonElement item, string at, List<string> problems)
    {
        void Check(bool valid, string field, string message)
        { if (!valid) { problems.Add(at + field + ": " + message); } }
        if (item.ValueKind != JsonValueKind.Object)
        { Check(false, "", "must be an object."); return; }
        string? Text(string field) => item.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? kind = Text("kind"), type = Text("dimension_type");
        string required = kind switch
        {
            "hyperlink" => "kind position link contents",
            "qr_code" => "kind position size_mm text",
            "spline" => "kind points",
            "sector" => "kind center radius_mm start_angle end_angle",
            
            
            "dimension" => "kind dimension_type end " + (type == "circle" ? "arc" : type is "linear" or "angular" ? "first second" : "points"),
            _ => "kind"
        };
        Check(Kinds.Contains(kind, StringComparer.Ordinal), "/kind", "unsupported annotation kind.");
        string[] allowed = (required + (kind is "hyperlink" or "qr_code" or "spline" or "sector" ? " pen layer" : "") + (kind == "placeholder" ? " records variables" : "")).Split(' ');
        foreach (string field in required.Split(' '))
        { Check(item.TryGetProperty(field, out _), "/" + field, "is required."); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty field in item.EnumerateObject())
        { Check(seen.Add(field.Name) && allowed.Contains(field.Name, StringComparer.Ordinal), "/" + field.Name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal), "unknown or duplicate member."); }
        foreach (string field in _textFields.Where(n => allowed.Contains(n, StringComparer.Ordinal)))
        { Check(!string.IsNullOrWhiteSpace(Text(field)), "/" + field, "requires a nonempty string."); }
        foreach (string field in _listFields)
        {
            if (item.TryGetProperty(field, out JsonElement list))
            { Check(list.ValueKind == JsonValueKind.Array && list.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())), "/" + field, "requires an array of nonempty strings."); }
        }
        if (kind == "qr_code")
        { Check(item.TryGetProperty("size_mm", out JsonElement size) && size.ValueKind == JsonValueKind.Number && size.TryGetDouble(out double n) && double.IsFinite(n) && n > 0 && n <= 10000, "/size_mm", "requires a finite positive size up to 10000 mm."); }
        var primitive = new Dictionary<string, object?> { ["kind"] = kind == "spline" ? "polyline" : kind == "sector" ? "arc" : "path_text" };
        foreach (string field in _geometryFields)
        { if (item.TryGetProperty(field, out JsonElement value)) { primitive[field] = value; } }
        if (kind is not ("spline" or "sector"))
        { primitive["contents"] = item.TryGetProperty("contents", out JsonElement contents) ? contents : new Dictionary<string, string> { ["??_??"] = "annotation" }; }
        var pointPaths = new List<string>();
        JsonElement request = JsonSerializer.SerializeToElement(new { project = "P", page = "P", items = new[] { primitive } });
        foreach (string problem in PlacementCreatePlanning.Validate(request))
        {
            string mapped = problem;
            for (int i = pointPaths.Count - 1; i >= 0; i--)
            { mapped = mapped.Replace("/items/0/points/" + i + ":", at + pointPaths[i] + ":", StringComparison.Ordinal).Replace("/items/0/points/" + i + "/", at + pointPaths[i] + "/", StringComparison.Ordinal); }
            problems.Add(mapped.Replace("/items/0", at, StringComparison.Ordinal));
        }
    }

    internal static PlacementText Request(JsonElement item)
    {
        string kind = item.GetProperty("kind").GetString()!;
        double[] Point(string field) => item.TryGetProperty(field, out JsonElement value) ? value.Deserialize<double[]>()! : [];
        string? Text(string field) => item.TryGetProperty(field, out JsonElement value) ? value.GetString() : null;
        return new()
        {
            Kind = kind,
            Position = kind == "sector" ? Point("center") : kind == "dimension" ? Point("end") : kind == "spline" ? item.GetProperty("points")[0].Deserialize<double[]>()! : Point("position"),
            Points = item.TryGetProperty("points", out JsonElement points) ? points.Deserialize<double[][]>()! : kind == "dimension" && Text("dimension_type") != "circle" ? [Point("first"), Point("second")] : [],
            EndPosition = Point("end"),
            Name = Text(kind == "macro_box" ? "macro_name" : "name"),
            Link = Text("link"),
            Text = Text("text"),
            Contents = item.TryGetProperty("contents", out JsonElement contents) ? contents.Deserialize<Dictionary<string, string>>()! : [],
            RadiusMm = item.TryGetProperty("radius_mm", out JsonElement radius) ? radius.GetDouble() : 0,
            StartAngle = item.TryGetProperty("start_angle", out JsonElement start) ? start.GetDouble() : 0,
            EndAngle = item.TryGetProperty("end_angle", out JsonElement end) ? end.GetDouble() : 0,
            SizeMm = item.TryGetProperty("size_mm", out JsonElement size) ? size.GetDouble() : 0,
            DimensionType = Text("dimension_type"),
            Arc = item.TryGetProperty("arc", out JsonElement arc) ? arc.Deserialize<DrawingLocator>(_json) : null,
            Records = item.TryGetProperty("records", out JsonElement records) ? records.Deserialize<string[]>() : null,
            Variables = item.TryGetProperty("variables", out JsonElement variables) ? variables.Deserialize<string[]>() : null,
            Layer = item.TryGetProperty("layer", out JsonElement layer) ? layer.GetInt32() : null,
            Pen = item.TryGetProperty("pen", out JsonElement pen) ? pen.Deserialize<Dictionary<string, double>>() : null
        };
    }
}

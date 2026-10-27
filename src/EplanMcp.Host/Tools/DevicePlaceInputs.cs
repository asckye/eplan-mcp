using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

internal static class DevicePlaceInputs
{
    internal static void Configure(JsonObject schema)
    {
        schema["oneOf"] = JsonNode.Parse("""
            [{"required":["part"],"not":{"anyOf":[{"required":["library"]},{"required":["symbol"]},{"required":["symbol_variant"]},{"required":["display_properties"]},{"required":["connection_designations"]}]}},
             {"required":["library","symbol","symbol_variant"],"not":{"anyOf":[{"required":["part"]},{"required":["variant"]}]}}]
            """);
        schema["properties"]!["display_properties"] = JsonNode.Parse("""
            {"type":["array","null"],"maxItems":8,"items":{"type":"object","additionalProperties":false,"required":["property_id","offset_x_mm","offset_y_mm"],"properties":{
            "property_id":{"type":"integer","enum":[20214,20213,20211,20215,20058,20011,20027]},"offset_x_mm":{"type":"number"},"offset_y_mm":{"type":"number"}}}}
            """);
        schema["properties"]!["symbol_variant"]!["minimum"] = 0;
        schema["properties"]!["connection_designations"]!["items"] = JsonNode.Parse("""{"type":"string","minLength":1,"maxLength":64}""");
    }

    internal static List<string> Check(DevicePlaceArgs args, JsonElement[]? display)
    {
        List<string> problems = ToolArguments.WriteProjectViolations(args.Project);
        bool symbol = args.Library is not null || args.Symbol is not null || args.SymbolVariant is not null;
        bool part = !string.IsNullOrWhiteSpace(args.Part);
        if (!double.IsFinite(args.X) || !double.IsFinite(args.Y))
        { problems.Add("/x: x and y must be finite millimetre coordinates."); }
        if (symbol ? part || args.Variant is not null || string.IsNullOrWhiteSpace(args.Library) || string.IsNullOrWhiteSpace(args.Symbol) || args.SymbolVariant is null or < 0 : !part)
        { problems.Add("/part: specify either part with optional variant, or library, symbol and nonnegative symbol_variant, exclusively."); }
        if (!symbol && (display is not null || args.ConnectionDesignations is not null))
        { problems.Add("/display_properties: display_properties and connection_designations require a symbol source."); }
        if (args.Library?.Any(char.IsControl) == true || args.Symbol?.Any(char.IsControl) == true)
        { problems.Add("/library: library and symbol must not contain control characters."); }
        if (args.ConnectionDesignations?.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 64 || s.Any(char.IsControl)) == true)
        { problems.Add("/connection_designations: each pin needs nonempty text of at most 64 characters without controls."); }
        if (display is null)
        { return problems; }
        if (display.Length > 8)
        { problems.Add("/display_properties: at most 8 placements are allowed."); return problems; }
        args.DisplayProperties = [];
        for (int i = 0; i < display.Length; i++)
        {
            JsonElement item = display[i];
            string at = "/display_properties/" + i.ToString(CultureInfo.InvariantCulture);
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 3
                || item.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 3
                || !item.TryGetProperty("property_id", out JsonElement id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int number)
                || number is not (20214 or 20213 or 20211 or 20215 or 20058 or 20011 or 20027)
                || !Offset(item, "offset_x_mm", out double x) || !Offset(item, "offset_y_mm", out double y)
                || !double.IsFinite(args.X + x) || !double.IsFinite(args.Y + y))
            { problems.Add(at + ": require allowed property_id and finite offset_x_mm, offset_y_mm; no other or duplicate members."); continue; }
            args.DisplayProperties.Add(new() { PropertyId = number, OffsetXmm = x, OffsetYmm = y });
        }
        return problems;
    }

    private static bool Offset(JsonElement item, string name, out double value)
    {
        value = 0;
        return item.TryGetProperty(name, out JsonElement node) && node.ValueKind == JsonValueKind.Number && node.TryGetDouble(out value) && double.IsFinite(value);
    }
}

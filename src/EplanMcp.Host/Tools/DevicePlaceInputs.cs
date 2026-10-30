using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

internal static partial class DevicePlaceInputs
{
    internal static void Configure(JsonObject schema)
    {
        ((JsonArray)schema["required"]!).Remove("tag");
        schema["oneOf"] = JsonNode.Parse("""
            [{"required":["part","tag"],"propertyNames":{"not":{"enum":["terminal_strip","terminal_number","terminal_strip_text","library","symbol","symbol_variant","display_properties","connection_designations","location_box","corner_x","corner_y"]}}},
             {"required":["library","symbol","symbol_variant","tag"],"propertyNames":{"not":{"enum":["terminal_strip","terminal_number","terminal_strip_text","part","variant","location_box","corner_x","corner_y"]}}},
             {"required":["location_box","corner_x","corner_y"],"propertyNames":{"not":{"enum":["terminal_strip","terminal_number","terminal_strip_text","tag","part","variant","library","symbol","symbol_variant","display_properties","connection_designations"]}}},
             {"required":["terminal_strip","terminal_number"],"propertyNames":{"not":{"enum":["tag","part","variant","library","symbol","location_box","corner_x","corner_y","display_properties","connection_designations"]}}}]
            """);
        schema["properties"]!["display_properties"] = JsonNode.Parse("""
            {"type":["array","null"],"maxItems":8,"items":{"type":"object","additionalProperties":false,"required":["property_id","offset_x_mm","offset_y_mm"],"properties":{
            "property_id":{"type":"integer","enum":[20214,20213,20211,20215,20058,20011,20027,20400,20403,20301]},"offset_x_mm":{"type":"number"},"offset_y_mm":{"type":"number"}}}}
            """);
        schema["dependentRequired"] = JsonNode.Parse("""{"plc_connection_point":["library","symbol","symbol_variant","tag","plc_address","plc_symbolic_address","plc_text"],"plc_address":["plc_connection_point"],"plc_symbolic_address":["plc_connection_point"],"plc_text":["plc_connection_point"]}""");
        foreach (string field in new[] { "plc_connection_point", "plc_address", "plc_symbolic_address", "plc_text" })
        { schema["properties"]![field]!["minLength"] = 1; schema["properties"]![field]!["maxLength"] = 256; }
        schema["properties"]!["plc_connection_point"]!["maxLength"] = 64;
        schema["properties"]!["terminal_number"]!["minLength"] = 1;
        schema["properties"]!["terminal_number"]!["maxLength"] = 64;
        schema["properties"]!["terminal_strip_text"]!["maxLength"] = 256;
        schema["properties"]!["symbol_variant"]!["minimum"] = 0;
        schema["properties"]!["connection_designations"]!["items"] = JsonNode.Parse("""{"type":"string","minLength":1,"maxLength":64}""");
        foreach (JsonObject property in schema["properties"]!.AsObject().Select(p => p.Value).OfType<JsonObject>())
        { if (property["default"] is null) { property.Remove("default"); } }
    }

    internal static List<string> Check(DevicePlaceArgs args, JsonElement[]? display)
    {
        List<string> problems = ToolArguments.WriteProjectViolations(args.Project);
        foreach ((string field, string? text) in new[] { ("plc_text", args.PlcText), ("terminal_strip_text", args.TerminalStripText) })
        {
            if (text is not null && StoredLanguage().IsMatch(text))
            { problems.Add("/" + field + ": use plain language-independent text (??_??), without serialized language entries; do not mix it with real languages (A54)."); }
        }
        if (args.PlcConnectionPoint is not null || args.PlcAddress is not null || args.PlcSymbolicAddress is not null || args.PlcText is not null)
        {
            if (args.Library is null || args.TerminalStrip is not null || args.LocationBox is not null
                || new[] { args.PlcConnectionPoint, args.PlcAddress, args.PlcSymbolicAddress, args.PlcText }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256 || s.Any(char.IsControl))
                || args.PlcConnectionPoint is { Length: > 64 } || args.PlcConnectionPoint!.StartsWith(':'))
            { problems.Add("/plc_connection_point: require a symbol source, existing PLC tag and four nonempty PLC fields without controls (point at most 64 characters, others 256); omit a leading colon."); }
        }
        if (args.TerminalStrip is not null)
        {
            if (args.Part is not null || args.Variant is not null || args.Library is not null || args.Symbol is not null || args.Tag is not null
                || args.LocationBox is not null || args.CornerX is not null || args.CornerY is not null || display is not null || args.ConnectionDesignations is not null || args.SymbolVariant is < 0)
            { problems.Add("/terminal_strip: use an exclusive terminal source with optional symbol_variant; omit tag and other sources."); }
            if (args.TerminalStrip.Length is < 2 or > 128 || args.TerminalStrip.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == ':')
                || args.TerminalStrip.LastIndexOf('-') < 0 || args.TerminalStrip[args.TerminalStrip.Length - 1] == '-')
            { problems.Add("/terminal_strip: require a short or full device tag, at most 128 characters, without whitespace, controls or colon."); }
            if (args.TerminalNumber is not { Length: > 0 and <= 64 } number || number.Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '.')))
            { problems.Add("/terminal_number: require 1-64 letters, digits, '_' or '.'."); }
            if (args.TerminalStripText is { } text && (string.IsNullOrWhiteSpace(text) || text.Length > 256 || text.Any(char.IsControl)))
            { problems.Add("/terminal_strip_text: require nonempty text of at most 256 characters without controls."); }
            if (!double.IsFinite(args.X) || !double.IsFinite(args.Y))
            { problems.Add("/x: x and y must be finite millimetre coordinates."); }
            return problems;
        }
        if (args.TerminalNumber is not null || args.TerminalStripText is not null)
        { problems.Add("/terminal_strip: terminal_number and terminal_strip_text require a terminal source."); }
        if (args.LocationBox is { } structure)
        {
            if (structure.Length is < 2 or > 65 || structure[0] != '+' || structure.Skip(1).Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '.'))
                || args.Part is not null || args.Variant is not null || args.Library is not null || args.Symbol is not null || args.SymbolVariant is not null
                || args.Tag is not null || display is not null || args.ConnectionDesignations is not null)
            { problems.Add("/location_box: use an exclusive +location identifier (1-64 letters, digits, '_' or '.'); no tag or symbol options."); }
            if (!double.IsFinite(args.X) || !double.IsFinite(args.Y) || args.X < 0 || args.Y < 0
                || args.CornerX is not { } x || args.CornerY is not { } y || !double.IsFinite(x) || !double.IsFinite(y) || x <= args.X || y <= args.Y)
            { problems.Add("/corner_x: require finite lower-left x/y and upper-right corner_x/corner_y, with positive width and height."); }
            return problems;
        }
        if (args.CornerX is not null || args.CornerY is not null)
        { problems.Add("/location_box: corner_x and corner_y require a location_box source."); }
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
                || number is not (20214 or 20213 or 20211 or 20215 or 20058 or 20011 or 20027 or 20400 or 20403 or 20301)
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

    [GeneratedRegex(@"(?:^|;)([A-Za-z]{2}_[A-Za-z]{2}|\?\?_\?\?)@", RegexOptions.CultureInvariant)]
    private static partial Regex StoredLanguage();
}

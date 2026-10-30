using System.Globalization;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

internal static class AcceptanceDrawingList
{
    internal const int MaxBytes = 256 * 1024;

    internal static JsonElement Read(string json)
    {
        Require(System.Text.Encoding.UTF8.GetByteCount(json) <= MaxBytes, "", "drawing list exceeds 256 KiB");
        using JsonDocument document = JsonDocument.Parse(json, new() { MaxDepth = 32 });
        JsonElement root = document.RootElement;
        FiniteNumbers(root, "");
        Object(root, "", "version page counterpart_page location_boxes devices plc terminal_strip wires nodes cables interruption_points texts expected_connections", "interruption_support");
        Integer(root.GetProperty("version"), "/version", 1, 1);
        Text(root, "page", "");
        Text(root, "counterpart_page", "");
        Require(root.GetProperty("page").GetString() != root.GetProperty("counterpart_page").GetString(), "/counterpart_page", "must differ from page");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pins = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Identity(JsonElement item, string at)
        {
            Text(item, "id", at);
            Require(ids.Add(item.GetProperty("id").GetString()!), at + "/id", "duplicate item id");
        }
        void Device(JsonElement item, string at, string extra = "")
        {
            Object(item, at, "id tag library symbol variant position connection_designations" + extra, "contact_kind");
            Identity(item, at);
            foreach (string key in "tag library symbol".Split(' '))
            { Text(item, key, at); }
            Require(tags.Add(item.GetProperty("tag").GetString()!), at + "/tag", "duplicate tag");
            Integer(item.GetProperty("variant"), at + "/variant", 0, int.MaxValue);
            Position(item.GetProperty("position"), at + "/position");
            string[] designations = Strings(item.GetProperty("connection_designations"), at + "/connection_designations", 0, 32);
            Require(designations.Distinct(StringComparer.Ordinal).Count() == designations.Length, at + "/connection_designations", "must be unique for pin resolution");
            pins.Add(item.GetProperty("id").GetString()!, designations);
            if (item.TryGetProperty("contact_kind", out JsonElement kind))
            { Require(kind.ValueKind == JsonValueKind.String && kind.GetString() is "no" or "nc", at + "/contact_kind", "must be no or nc"); }
        }

        Each(root, "location_boxes", "", (box, at) =>
        {
            Object(box, at, "id structure corners", "");
            Identity(box, at);
            Text(box, "structure", at);
            Require(box.GetProperty("structure").GetString()!.StartsWith('+'), at + "/structure", "must start with +");
            Points(box.GetProperty("corners"), at + "/corners", 2, 2, false);
            JsonElement corners = box.GetProperty("corners");
            Require(corners[0][0].GetDouble() < corners[1][0].GetDouble() && corners[0][1].GetDouble() < corners[1][1].GetDouble(), at + "/corners", "must be lower-left and upper-right");
        });
        Each(root, "devices", "", (item, at) => Device(item, at));
        JsonElement plc = root.GetProperty("plc");
        Object(plc, "/plc", "box points", "");
        Device(plc.GetProperty("box"), "/plc/box");
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        Each(plc, "points", "/plc", (point, at) =>
        {
            Object(point, at, "id tag position connection_designations connection_point address symbolic_address function_text direction", "display_properties");
            Identity(point, at);
            foreach (string key in "tag connection_point address symbolic_address function_text".Split(' '))
            { Text(point, key, at); }
            Require(tags.Add(point.GetProperty("tag").GetString()!), at + "/tag", "duplicate tag");
            Position(point.GetProperty("position"), at + "/position");
            string[] names = Strings(point.GetProperty("connection_designations"), at + "/connection_designations", 1, 1);
            Require(names[0] == point.GetProperty("connection_point").GetString(), at + "/connection_designations", "must equal the PLC connection point");
            Require(point.GetProperty("tag").GetString() == plc.GetProperty("box").GetProperty("tag").GetString() + ":" + names[0], at + "/tag", "must name a connection point on the PLC box");
            pins.Add(point.GetProperty("id").GetString()!, names);
            Require(point.GetProperty("direction").ValueKind == JsonValueKind.String && point.GetProperty("direction").GetString() is "Up" or "Down" or "Left" or "Right", at + "/direction", "must be a native pin direction");
            if (point.TryGetProperty("display_properties", out _))
            {
                var displayed = new HashSet<int>();
                Each(point, "display_properties", at, (display, pointer) =>
                {
                    Object(display, pointer, "property_id offset_x_mm offset_y_mm", "");
                    int id = Integer(display.GetProperty("property_id"), pointer + "/property_id", 0, int.MaxValue);
                    Require(id is 20400 or 20403 or 20011 or 20301 && displayed.Add(id), pointer + "/property_id", "must be a distinct PLC display property");
                    foreach (string axis in "offset_x_mm offset_y_mm".Split(' '))
                    { Require(display.GetProperty(axis).ValueKind == JsonValueKind.Number, pointer + "/" + axis, "must be a finite number"); }
                }, 0, 8);
            }
            Require(addresses.Add(point.GetProperty("address").GetString()!), at + "/address", "duplicate PLC address");
        });
        JsonElement strip = root.GetProperty("terminal_strip");
        Object(strip, "/terminal_strip", "id tag function_text position terminals", "");
        Identity(strip, "/terminal_strip");
        Text(strip, "tag", "/terminal_strip");
        Text(strip, "function_text", "/terminal_strip");
        Position(strip.GetProperty("position"), "/terminal_strip/position");
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        Each(strip, "terminals", "/terminal_strip", (terminal, at) =>
        {
            Device(terminal, at, " number");
            Text(terminal, "number", at);
            Require(numbers.Add(terminal.GetProperty("number").GetString()!), at + "/number", "duplicate terminal number");
            Require(terminal.GetProperty("tag").GetString() == strip.GetProperty("tag").GetString() + ":" + terminal.GetProperty("number").GetString(), at + "/tag", "must name a terminal on the strip");
            Require(terminal.GetProperty("library").GetString() == "IEC_symbol" && terminal.GetProperty("symbol").GetString() == "X", at + "/symbol", "terminal placement requires IEC_symbol/X");
        });
        void Endpoint(JsonElement endpoint, string at)
        {
            Object(endpoint, at, "item pin", "");
            Text(endpoint, "item", at);
            Text(endpoint, "pin", at);
            Require(pins.TryGetValue(endpoint.GetProperty("item").GetString()!, out string[]? names)
                && names.Contains(endpoint.GetProperty("pin").GetString()!, StringComparer.Ordinal), at, "must reference an item and one of its connection designations");
        }
        var wireIds = new HashSet<string>(StringComparer.Ordinal);
        Each(root, "wires", "", (wire, at) =>
        {
            Object(wire, at, "id from to points", "");
            Identity(wire, at);
            wireIds.Add(wire.GetProperty("id").GetString()!);
            Endpoint(wire.GetProperty("from"), at + "/from");
            Endpoint(wire.GetProperty("to"), at + "/to");
            JsonElement path = wire.GetProperty("points");
            Array(path, at + "/points", 2, 32);
            for (int i = 0; i < path.GetArrayLength(); i++)
            {
                if (path[i].ValueKind == JsonValueKind.String)
                { Require(i == 0 && path[i].GetString() == "from" || i == path.GetArrayLength() - 1 && path[i].GetString() == "to", at + "/points/" + i.ToString(CultureInfo.InvariantCulture), "only endpoint anchors from/to are allowed"); }
                else
                { Position(path[i], at + "/points/" + i.ToString(CultureInfo.InvariantCulture)); }
                if (i > 0 && path[i].ValueKind == JsonValueKind.Array && path[i - 1].ValueKind == JsonValueKind.Array)
                { Require((path[i][0].GetDouble() == path[i - 1][0].GetDouble()) != (path[i][1].GetDouble() == path[i - 1][1].GetDouble()), at + "/points/" + i.ToString(CultureInfo.InvariantCulture), "segment must be nonzero and orthogonal"); }
            }
        });
        Each(root, "nodes", "", (node, at) =>
        {
            Object(node, at, "id kind position directions", "");
            Identity(node, at);
            Position(node.GetProperty("position"), at + "/position");
            Text(node, "kind", at);
            string? kind = node.GetProperty("kind").GetString();
            Require(kind is "corner" or "t_node" or "potential_point", at + "/kind", "unsupported connection symbol");
            string[] directions = Strings(node.GetProperty("directions"), at + "/directions", 1, 3);
            Require(directions.Length == (kind == "corner" ? 2 : kind == "t_node" ? 3 : 1)
                && directions.Distinct(StringComparer.Ordinal).Count() == directions.Length
                && directions.All(d => d is "Up" or "Down" or "Left" or "Right")
                && (kind != "corner" || directions.Any(d => d is "Up" or "Down") && directions.Any(d => d is "Left" or "Right")), at + "/directions", "must match the node kind");
        });
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        Each(root, "cables", "", (cable, at) =>
        {
            Object(cable, at, "id tag type_text core_count cross_section_mm2 points cores", "");
            Identity(cable, at);
            Text(cable, "tag", at);
            Text(cable, "type_text", at);
            int count = Integer(cable.GetProperty("core_count"), at + "/core_count", 1, 50);
            Positive(cable.GetProperty("cross_section_mm2"), at + "/cross_section_mm2", double.MaxValue);
            Points(cable.GetProperty("points"), at + "/points", 2, 2, true);
            var labels = new HashSet<string>(StringComparer.Ordinal);
            Each(cable, "cores", at, (core, pointer) =>
            {
                Object(core, pointer, "wire label colour", "");
                foreach (string key in "wire label colour".Split(' '))
                { Text(core, key, pointer); }
                string wire = core.GetProperty("wire").GetString()!;
                Require(wireIds.Contains(wire) && assigned.Add(wire), pointer + "/wire", "must reference one unassigned wire");
                Require(labels.Add(core.GetProperty("label").GetString()!), pointer + "/label", "duplicate core label");
                string label = core.GetProperty("label").GetString()!;
                Require(label.All(char.IsAsciiDigit) ? int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                    && number >= 1 && number <= count && label == number.ToString(CultureInfo.InvariantCulture)
                    : label.Length <= 32 && char.IsAsciiLetter(label[0]) && label.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'), pointer + "/label", "must be a valid numeric or literal core label");
                Require("BK BN RD OG YE GN BU VT GY WH PK TQ GNYE".Split(' ').Contains(core.GetProperty("colour").GetString(), StringComparer.Ordinal), pointer + "/colour", "unsupported colour");
            }, 1, count);
        });
        Require(root.GetProperty("wires").GetArrayLength() + root.GetProperty("nodes").GetArrayLength() + root.GetProperty("cables").GetArrayLength() <= 50, "/cables", "shared wire, node and cable batch exceeds 50 items");
        Require(assigned.Count <= 50, "/cables", "shared cable batch exceeds 50 cores");
        var interruptionNames = new HashSet<string>(StringComparer.Ordinal);
        Each(root, "interruption_points", "", (point, at) =>
        {
            Object(point, at, "id name position direction counterpart_position counterpart_direction", "");
            Identity(point, at);
            Text(point, "name", at);
            Require(interruptionNames.Add(point.GetProperty("name").GetString()!), at + "/name", "duplicate interruption name");
            Position(point.GetProperty("position"), at + "/position");
            Position(point.GetProperty("counterpart_position"), at + "/counterpart_position");
            foreach (string key in "direction counterpart_direction".Split(' '))
            { Text(point, key, at); Require(point.GetProperty(key).GetString() is "Up" or "Down" or "Left" or "Right", at + "/" + key, "invalid direction"); }
        }, 0, 49);
        Require(root.GetProperty("interruption_points").GetArrayLength() == 0 || root.TryGetProperty("interruption_support", out _), "/interruption_support", "paired page batches require explicit generation support");
        if (root.TryGetProperty("interruption_support", out JsonElement support))
        {
            Object(support, "/interruption_support", "page counterpart_page", "");
            foreach (string key in new[] { "page", "counterpart_page" })
            {
                string at = "/interruption_support/" + key;
                JsonElement item = support.GetProperty(key);
                Object(item, at, "device potential_point", "");
                Device(item.GetProperty("device"), at + "/device");
                Require(item.GetProperty("device").GetProperty("connection_designations").GetArrayLength() > 0, at + "/device/connection_designations", "generation support requires a pin");
                JsonElement node = item.GetProperty("potential_point");
                Object(node, at + "/potential_point", "position directions", "");
                Position(node.GetProperty("position"), at + "/potential_point/position");
                string[] directions = Strings(node.GetProperty("directions"), at + "/potential_point/directions", 1, 1);
                Require(directions[0] is "Up" or "Down" or "Left" or "Right", at + "/potential_point/directions", "invalid direction");
            }
        }
        Each(root, "texts", "", (text, at) =>
        {
            Object(text, at, "id position text height_mm", "");
            Identity(text, at);
            Text(text, "text", at);
            Position(text.GetProperty("position"), at + "/position");
            Positive(text.GetProperty("height_mm"), at + "/height_mm", 100);
        });
        Each(root, "expected_connections", "", (connection, at) =>
        {
            Object(connection, at, "from to", "");
            Endpoint(connection.GetProperty("from"), at + "/from");
            Endpoint(connection.GetProperty("to"), at + "/to");
        });
        return root.Clone();
    }

    private static void FiniteNumbers(JsonElement node, string at)
    {
        Require(node.ValueKind != JsonValueKind.Number || node.TryGetDecimal(out _), at, "must be a finite decimal");
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in node.EnumerateObject())
            {
                string pointer = Child(at, property.Name);
                Require(names.Add(property.Name), pointer, "duplicate member");
                FiniteNumbers(property.Value, pointer);
            }
        }
        if (node.ValueKind == JsonValueKind.Array)
        { int i = 0; foreach (JsonElement item in node.EnumerateArray()) { FiniteNumbers(item, Child(at, (i++).ToString(CultureInfo.InvariantCulture))); } }
    }

    private static void Object(JsonElement node, string at, string required, string optional)
    {
        Require(node.ValueKind == JsonValueKind.Object, at, "must be an object");
        string[] keys = required.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string key in keys)
        { Require(node.TryGetProperty(key, out _), Child(at, key), "is required"); }
        foreach (JsonProperty property in node.EnumerateObject())
        { Require(keys.Contains(property.Name, StringComparer.Ordinal) || optional.Split(' ').Contains(property.Name, StringComparer.Ordinal), Child(at, property.Name), "unknown member"); }
    }

    private static void Text(JsonElement node, string key, string at) => Require(node.GetProperty(key).ValueKind == JsonValueKind.String
        && node.GetProperty(key).GetString() is { Length: > 0 and <= 200 } value && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl), Child(at, key), "must be nonempty text, at most 200 characters, without controls");

    private static string[] Strings(JsonElement node, string at, int min, int max)
    {
        Array(node, at, min, max);
        Require(node.EnumerateArray().All(n => n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())), at, "must contain nonempty strings");
        return node.EnumerateArray().Select(n => n.GetString()!).ToArray();
    }

    private static void Position(JsonElement node, string at)
    {
        Array(node, at, 2, 2);
        Require(node.EnumerateArray().All(n => n.ValueKind == JsonValueKind.Number && n.GetDouble() is >= 0 and <= 10000), at, "must contain two finite millimetre coordinates in 0..10000");
    }

    private static int Integer(JsonElement node, string at, int min, int max)
    {
        Require(node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out int number) && number >= min && number <= max, at, "integer is outside the allowed range");
        return node.GetInt32();
    }

    private static void Positive(JsonElement node, string at, double max) => Require(node.ValueKind == JsonValueKind.Number && node.GetDouble() > 0 && node.GetDouble() <= max, at, "must be a positive finite number within the allowed range");

    private static void Points(JsonElement node, string at, int min, int max, bool orthogonal)
    {
        Array(node, at, min, max);
        for (int i = 0; i < node.GetArrayLength(); i++)
        {
            Position(node[i], Child(at, i.ToString(CultureInfo.InvariantCulture)));
            if (orthogonal && i > 0)
            { Require((node[i][0].GetDouble() == node[i - 1][0].GetDouble()) != (node[i][1].GetDouble() == node[i - 1][1].GetDouble()), Child(at, i.ToString(CultureInfo.InvariantCulture)), "segment must be nonzero and orthogonal"); }
        }
    }

    private static void Each(JsonElement node, string key, string at, Action<JsonElement, string> action, int min = 0, int max = 50)
    {
        JsonElement array = node.GetProperty(key);
        string pointer = Child(at, key);
        Array(array, pointer, min, max);
        int i = 0;
        foreach (JsonElement item in array.EnumerateArray())
        { action(item, Child(pointer, (i++).ToString(CultureInfo.InvariantCulture))); }
    }

    private static void Array(JsonElement node, string at, int min, int max) => Require(node.ValueKind == JsonValueKind.Array && node.GetArrayLength() >= min && node.GetArrayLength() <= max, at, "array length is outside the allowed range");
    private static string Child(string at, string key) => at + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static void Require(bool condition, string at, string reason)
    { if (!condition) { throw new JsonException(at + ": " + reason); } }
}

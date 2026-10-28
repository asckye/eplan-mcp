using System.Text.Json.Nodes;

namespace EplanMcp.Host.Tools;

internal static class ConnectionCreateSchema
{
    internal static JsonObject Items()
    {
        JsonObject Text(int max = 200) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = max };
        JsonObject Integer(int min, int max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
        JsonObject Array(JsonObject item, int min, int max) => new() { ["type"] = "array", ["items"] = item, ["minItems"] = min, ["maxItems"] = max };
        JsonObject Object(JsonObject properties) => new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]) };
        JsonObject Kind(string kind) => new() { ["const"] = kind };
        JsonObject Point() => Array(new() { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 10000 }, 2, 2);
        JsonObject Locator() => Object(new() { ["project"] = Text(32768), ["page"] = Text(1024), ["type"] = Integer(1, int.MaxValue), ["oid"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 }, ["digest"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9a-f]{64}$" } });
        JsonObject Pin() => Object(new() { ["locator"] = Locator(), ["pin_index"] = Integer(0, int.MaxValue) });
        JsonObject Node(string kind, int count) => Object(new() { ["kind"] = Kind(kind), ["position"] = Point(), ["directions"] = new JsonObject { ["type"] = "array", ["minItems"] = count, ["maxItems"] = count, ["uniqueItems"] = true, ["items"] = new JsonObject { ["enum"] = new JsonArray("Up", "Down", "Left", "Right") } } });
        JsonObject wire = Object(new() { ["kind"] = Kind("wire"), ["from"] = Pin(), ["to"] = Pin(), ["points"] = Array(Point(), 2, 32) });
        JsonObject pages = Array(Text(1024), 1, 20);
        pages["uniqueItems"] = true;
        JsonObject interruption = Object(new() { ["kind"] = Kind("interruption_point"), ["name"] = Text(), ["position"] = Point(), ["direction"] = new JsonObject { ["enum"] = new JsonArray("Up", "Down", "Left", "Right") }, ["counterpart_pages"] = pages });
        JsonObject reference = new() { ["oneOf"] = new JsonArray(Object(new() { ["locator"] = Locator() }), Object(new() { ["item_index"] = Integer(0, 49) })) };
        JsonObject core = Object(new() { ["connection"] = reference, ["label"] = Text(32), ["colour"] = new JsonObject { ["enum"] = new JsonArray("BK", "BN", "RD", "OG", "YE", "GN", "BU", "VT", "GY", "WH", "PK", "TQ", "GNYE") } });
        JsonObject cable = Object(new() { ["kind"] = Kind("cable_definition"), ["points"] = Array(Point(), 2, 2), ["designation"] = Text(128), ["type_text"] = Text(), ["core_count"] = Integer(1, 50), ["cross_section_mm2"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0 }, ["cores"] = Array(core, 1, 50) });
        return Array(new() { ["oneOf"] = new JsonArray(wire, Node("corner", 2), Node("t_node", 3), Node("potential_point", 1), interruption, cable) }, 1, 50);
    }
}

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
        JsonObject Point() => new() { ["$ref"] = "#/$defs/p" };
        JsonObject Locator() => new() { ["$ref"] = "#/$defs/locator" };
        JsonObject Pin() => new() { ["$ref"] = "#/$defs/pin" };
        JsonObject Node(string kind, int count) => Object(new() { ["kind"] = Kind(kind), ["position"] = Point(), ["directions"] = new JsonObject { ["type"] = "array", ["minItems"] = count, ["maxItems"] = count, ["uniqueItems"] = true, ["items"] = new JsonObject { ["enum"] = new JsonArray("Up", "Down", "Left", "Right") } } });
        JsonObject wire = Object(new() { ["kind"] = Kind("wire"), ["from"] = Pin(), ["to"] = Pin(), ["points"] = Array(Point(), 2, 32) });
        wire["properties"]!["definition_point"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject { ["position"] = Point(), ["symbol"] = new JsonObject { ["enum"] = new JsonArray("CDP", "CDPNG"), ["default"] = "CDP" }, ["symbol_variant"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 0 }, ["color"] = Text(), ["cross_section"] = new JsonObject { ["type"] = "string", ["pattern"] = "^ *[+-]?(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)(?:[eE][+-]?[0-9]+)? *$" }, ["designation"] = Text() },
            ["anyOf"] = new JsonArray(new JsonObject { ["required"] = new JsonArray("color") }, new JsonObject { ["required"] = new JsonArray("cross_section") }, new JsonObject { ["required"] = new JsonArray("designation") }),
        };
        JsonObject definition = wire["properties"]!["definition_point"]!.DeepClone().AsObject();
        definition["properties"]!["kind"] = Kind("definition_point");
        definition["required"] = new JsonArray("kind", "position");
        JsonObject pages = Array(Text(1024), 1, 20);
        pages["uniqueItems"] = true;
        JsonObject interruption = Object(new() { ["kind"] = Kind("interruption_point"), ["name"] = Text(), ["position"] = Point(), ["direction"] = new JsonObject { ["enum"] = new JsonArray("Up", "Down", "Left", "Right") }, ["counterpart_pages"] = pages });
        interruption["properties"]!["sort_code"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 };
        interruption["properties"]!["symbol"] = Text(64);
        JsonObject tNode = Node("t_node", 3);
        tNode["properties"]!["variant"] = new JsonObject { ["type"] = "integer" };
        JsonObject reference = new() { ["oneOf"] = new JsonArray(Object(new() { ["locator"] = Locator() }), Object(new() { ["item_index"] = Integer(0, 49) })) };
        JsonObject core = Object(new() { ["connection"] = reference, ["label"] = Text(32), ["colour"] = new JsonObject { ["enum"] = new JsonArray("BK", "BN", "RD", "OG", "YE", "GN", "BU", "VT", "GY", "WH", "PK", "TQ", "GNYE") } });
        JsonObject cable = Object(new() { ["kind"] = Kind("cable_definition"), ["points"] = Array(Point(), 2, 2), ["designation"] = Text(128), ["type_text"] = Text(), ["core_count"] = Integer(1, 50), ["cross_section_mm2"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0 }, ["cores"] = Array(core, 1, 50) });
        cable["properties"]!["voltage"] = Text(64);
        JsonObject items = Array(new() { ["oneOf"] = new JsonArray(wire, Node("corner", 2), tNode, Node("potential_point", 1), interruption, cable, definition) }, 1, 50);
        items["$defs"] = new JsonObject { ["p"] = Array(new() { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 10000 }, 2, 2), ["pin"] = Object(new() { ["locator"] = Locator(), ["pin_index"] = Integer(0, int.MaxValue) }), ["locator"] = Object(new() { ["project"] = Text(32768), ["page"] = Text(1024), ["type"] = Integer(1, int.MaxValue), ["oid"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 }, ["digest"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9a-f]{64}$" } }) };
        return items;
    }
}

using System.Text.Json.Nodes;

namespace EplanMcp.Host.Tools;

internal static class ConnectionCreateSchema
{
    internal static JsonObject Items()
    {
        JsonObject Text(int max = 200) => max == 200 ? new() { ["$ref"] = "#/$defs/t" } : new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = max };
        JsonObject Integer(int min, int max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
        JsonObject Array(JsonObject item, int min, int max) => new() { ["type"] = "array", ["items"] = item, ["minItems"] = min, ["maxItems"] = max };
        JsonObject Object(JsonObject properties) => new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]) };
        JsonObject Kind(string kind) => new() { ["const"] = kind };
        JsonObject Point() => new() { ["$ref"] = "#/$defs/p" };
        JsonObject Locator() => new() { ["$ref"] = "#/$defs/l" };
        JsonObject Direction() => new() { ["$ref"] = "#/$defs/r" };
        JsonObject Pin() => new() { ["$ref"] = "#/$defs/i" };
        JsonObject Node(string kind, int count) => Object(new() { ["kind"] = Kind(kind), ["position"] = Point(), ["directions"] = new JsonObject { ["type"] = "array", ["minItems"] = count, ["maxItems"] = count, ["uniqueItems"] = true, ["items"] = Direction() } });
        JsonObject wire = Object(new() { ["kind"] = Kind("wire"), ["from"] = Pin(), ["to"] = Pin(), ["points"] = Array(Point(), 2, 32) });
        wire["properties"]!["definition_point"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject { ["position"] = Point(), ["symbol"] = new JsonObject { ["enum"] = new JsonArray("CDP", "CDPNG"), ["default"] = "CDP" }, ["symbol_variant"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 0 }, ["function_definition"] = Text(), ["representation_type"] = new JsonObject { ["enum"] = new JsonArray(0, 1) }, ["type_code"] = Text(), ["color"] = Text(), ["cross_section"] = Text(), ["designation"] = Text(), ["part"] = Text(), ["part_variant"] = new JsonObject { ["type"] = "string", ["default"] = "1" }, ["part_count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["default"] = 1 } },
        };
        JsonObject definitionFields = wire["properties"]!["definition_point"]!.AsObject();
        definitionFields["properties"]!["kind"] = Kind("definition_point");
        wire["properties"]!["definition_point"] = new JsonObject { ["$ref"] = "#/$defs/d", ["not"] = new JsonObject { ["required"] = new JsonArray("kind") } };
        JsonObject definition = new() { ["$ref"] = "#/$defs/d", ["properties"] = new JsonObject { ["kind"] = Kind("definition_point") }, ["required"] = new JsonArray("kind", "position") };
        JsonObject potential = Object(new() { ["kind"] = Kind("potential_definition"), ["position"] = Point() });
        potential["properties"]!["potential_name"] = Text();
        potential["properties"]!["signal_name"] = Text();
        potential["properties"]!["potential_value"] = Text();
        potential["properties"]!["potential_type"] = new JsonObject { ["type"] = "integer" };
        potential["properties"]!["color"] = Text();
        potential["properties"]!["cross_section"] = Text();
        potential["anyOf"] = new JsonArray(new JsonObject { ["required"] = new JsonArray("potential_name") }, new JsonObject { ["required"] = new JsonArray("signal_name") });
        JsonObject pages = Array(Text(1024), 1, 20);
        pages["uniqueItems"] = true;
        JsonObject interruption = Object(new() { ["kind"] = Kind("interruption_point"), ["name"] = Text(), ["position"] = Point(), ["direction"] = Direction(), ["counterpart_pages"] = pages });
        interruption["required"] = new JsonArray("kind", "name", "position", "direction");
        interruption["properties"]!["sort_code"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 };
        interruption["properties"]!["symbol"] = Text(64);
        interruption["properties"]!["property_arrangement"] = Text();
        interruption["properties"]!["symbol_variant"] = new JsonObject { ["type"] = "integer" };
        JsonObject tNode = Node("t_node", 3);
        tNode["properties"]!["variant"] = new JsonObject { ["type"] = "integer" };
        JsonObject reference = new() { ["oneOf"] = new JsonArray(Object(new() { ["locator"] = Locator() }), Object(new() { ["item_index"] = Integer(0, 49) })) };
        JsonObject core = Object(new() { ["connection"] = reference, ["label"] = Text(32), ["colour"] = new JsonObject { ["enum"] = new JsonArray("BK", "BN", "RD", "OG", "YE", "GN", "BU", "VT", "GY", "WH", "PK", "TQ", "GNYE") } });
        JsonObject cable = Object(new() { ["kind"] = Kind("cable_definition"), ["points"] = Array(Point(), 2, 2), ["designation"] = Text(128), ["type_text"] = Text(), ["core_count"] = Integer(1, 50), ["cross_section_mm2"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0 }, ["cores"] = Array(core, 1, 50) });
        cable["properties"]!["voltage"] = Text(64);
        JsonObject items = Array(new() { ["oneOf"] = new JsonArray(wire, Node("corner", 2), tNode, Node("potential_point", 1), interruption, cable, definition, potential) }, 1, 50);
        items["items"]!["type"] = "object";
        items["items"]!["additionalProperties"] = true; // Each alternative below enforces its own closed member set.
        foreach (JsonNode? branch in items["items"]!["oneOf"]!.AsArray())
        { branch!.AsObject().Remove("type"); }
        items["$defs"] = new JsonObject { ["r"] = new JsonObject { ["enum"] = new JsonArray("Up", "Down", "Left", "Right") }, ["d"] = definitionFields, ["t"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200 }, ["p"] = Array(new() { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 10000 }, 2, 2), ["i"] = Object(new() { ["locator"] = Locator(), ["pin_index"] = Integer(0, int.MaxValue) }), ["l"] = Object(new() { ["project"] = Text(32768), ["page"] = Text(1024), ["type"] = Integer(1, int.MaxValue), ["oid"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 }, ["digest"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9a-f]{64}$" } }) };
        return items;
    }
}

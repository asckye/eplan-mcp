using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using ModelContextProtocol.Server;
namespace EplanMcp.Host.Tools;

internal static class ObjectReadSchema
{
    internal static JsonElement Input(bool get)
    {
        var schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["object_type"],"properties":{"object_type":{"description":"Exact EPLAN object type to look up.","type":"string"},"project":{"description":"Name or path; default: the current project.","type":"string"},"limit":{"description":"1-200","type":"integer","minimum":1,"maximum":200,"default":50},"cursor":{"description":"nextCursor of the last result.","type":"string"}}}""")!;
        JsonNode fields = schema["properties"]!;
        fields["object_type"]!["enum"] = new JsonArray([.. ObjectReadTypes.Types.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s))]);
        if (get)
        {
            schema["required"]!.AsArray().Add("locator");
            fields["locator"] = DrawingLocatorSchema(described: true);
            fields["locator"]!["description"] = "Unchanged locator from eplan_object_find.";
            fields["language"] = JsonNode.Parse("""{"description":"Text language, e.g. zh_CN; default: EPLAN UI, then others","type":"string","pattern":"^[a-z]{2}_[A-Z]{2}$"}""");
            fields["response_format"] = JsonNode.Parse("""{"description":"Detailed adds display text and translations.","type":"string","enum":["concise","detailed"],"default":"concise"}""");
        }
        else
        {
            fields["page"] = new JsonObject { ["type"] = "string", ["description"] = "Full page name" };
            fields["name_contains"] = new JsonObject { ["type"] = "string", ["description"] = "Case-insensitive substring of the object name." };
        }
        return JsonSerializer.SerializeToElement(schema);
    }
    internal static JsonNode DrawingLocatorSchema(bool described = false)
    {
        JsonNode locator = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","type","oid","digest"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"type":{"type":"integer","minimum":1,"maximum":2147483647},"oid":{"type":"integer","minimum":1},"digest":{"type":"string"}}}""")!;
        if (!described)
        {
            return locator;
        }

        JsonNode parts = locator["properties"]!;
        parts["project"]!["description"] = "Resolved project path from the locator.";
        parts["page"]!["description"] = "Full page name";
        parts["type"]!["description"] = "EPLAN type number from the locator.";
        parts["oid"]!["description"] = "Object identifier from the locator.";
        parts["digest"]!["description"] = "Identity digest from eplan_object_find; copy unchanged.";
        return locator;
    }

    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        foreach (McpServerTool tool in tools.Where(t => t.ProtocolTool.Name is ObjectReadTools.FindTool or ObjectReadTools.GetTool))
        {
            bool get = tool.ProtocolTool.Name == ObjectReadTools.GetTool;
            tool.ProtocolTool.InputSchema = Input(get);
            JsonNode output = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","status","reason","items","cursor","truncated"],"properties":{"project":{"type":"string"},"status":{"enum":["supported","unsupported"]},"reason":{"type":["string","null"]},"items":{"type":"array","maxItems":200},"cursor":{"type":["string","null"]},"truncated":{"type":"boolean"}}}""")!;
            JsonNode fields = output["properties"]!;
            fields["items"]!["items"] = JsonNode.Parse(PropertyGetContract.Output.GetRawText())!["properties"]!["items"]!["items"]!.DeepClone();
            JsonNode row = fields["items"]!["items"]!;
            if (get)
            {
                row["required"] = new JsonArray("property_id", "property_name", "index", "status", "value", "language");
                row["properties"]!.AsObject().Remove("query");
                row["properties"]!.AsObject().Remove("target");
                row["properties"]!["property_name"] = new JsonObject { ["type"] = "string" };
                fields["locator"] = DrawingLocatorSchema();
                fields["hint"] = new JsonObject { ["type"] = "string" };
            }
            else
            { fields["items"]!["items"] = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["locator","name"],"properties":{"name":{"type":"string"},"location":{"type":"array","items":{"type":"number"}},"properties":{"type":"object","additionalProperties":{"type":"string"}}}}"""); fields["items"]!["items"]!["properties"]!["locator"] = DrawingLocatorSchema(); }
            tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(output);
        }
    }
}

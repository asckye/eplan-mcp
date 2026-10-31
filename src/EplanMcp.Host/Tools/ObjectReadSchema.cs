using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using ModelContextProtocol.Server;
namespace EplanMcp.Host.Tools;

internal static class ObjectReadSchema
{
    internal static JsonElement Input(bool get)
    {
        var schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["object_type"],"properties":{"object_type":{"type":"string"},"project":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":200,"default":50},"cursor":{"type":"string"}}}""")!;
        JsonNode fields = schema["properties"]!;
        fields["object_type"]!["enum"] = new JsonArray([.. PropertyCatalog.Entries.Where(e => !e.Obsolete).Select(e => e.ObjectType).Concat(ObjectReadTypes.FunctionTypes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s))]);
        if (get)
        {
            schema["required"]!.AsArray().Add("locator");
            fields["locator"] = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","type","oid","digest"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"type":{"type":"integer","minimum":1,"maximum":2147483647},"oid":{"type":"integer","minimum":1},"digest":{"type":"string"}}}""");
            fields["language"] = JsonNode.Parse("""{"type":"string","pattern":"^[a-z]{2}_[A-Z]{2}$"}""");
            fields["response_format"] = JsonNode.Parse("""{"type":"string","enum":["concise","detailed"],"default":"concise"}""");
        }
        else
        { fields["page"] = new JsonObject { ["type"] = "string" }; fields["name_contains"] = new JsonObject { ["type"] = "string" }; }
        return JsonSerializer.SerializeToElement(schema);
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
            { row["required"] = new JsonArray("property_id", "property_name", "index", "status", "value", "language"); row["properties"]!.AsObject().Remove("query"); row["properties"]!.AsObject().Remove("target"); row["properties"]!["property_name"] = new JsonObject { ["type"] = "string" }; }
            else
            { fields["items"]!["items"] = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["locator","name"],"properties":{"name":{"type":"string"}}}"""); fields["items"]!["items"]!["properties"]!["locator"] = JsonNode.Parse(Input(true).GetRawText())!["properties"]!["locator"]!.DeepClone(); }
            tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(output);
        }
    }
}

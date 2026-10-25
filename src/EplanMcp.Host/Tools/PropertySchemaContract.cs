using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using EplanMcp.Contracts;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Closed selector union and explicit nullable metadata; catalog facts never invent runtime constraints.</summary>
internal static class PropertySchemaContract
{
    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        McpServerTool tool = tools.Single(t => t.ProtocolTool.Name == PropertySchemaTools.Tool);
        var input = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
        JsonNode fields = input["properties"]!;
        fields["object_type"]!["enum"] = new JsonArray([.. PropertyCatalog.Entries.Select(e => e.ObjectType)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(n => (JsonNode?)JsonValue.Create(n))]);
        fields["name_contains"] = JsonNode.Parse("""{"type":"string","minLength":1,"maxLength":500}""");
        fields["properties"] = JsonNode.Parse("""
            {"type":"array","minItems":1,"maxItems":200,"items":{"type":"object","additionalProperties":false,
            "properties":{"property_id":{"type":"integer","minimum":1,"maximum":2147483647},"property_name":{"type":"string","minLength":1},"index":{"type":"integer","minimum":-2147483648,"maximum":2147483647}},
            "oneOf":[{"required":["property_id"],"not":{"required":["property_name"]}},{"required":["property_name"],"not":{"required":["property_id"]}}]}}
            """);
        input["not"] = JsonNode.Parse("""{"required":["properties","name_contains"]}""");
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(input);
        var output = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,"required":["catalog_version","object_type","items","total","cursor","truncated"],"properties":{
            "catalog_version":{"type":"string"},"object_type":{"type":"string"},"total":{"type":"integer","minimum":0},"cursor":{"type":["string","null"]},"truncated":{"type":"boolean"},
            "items":{"type":"array","maxItems":200,"items":{"type":"object","additionalProperties":false,"required":["property_id","property_name","read","write"],"properties":{
            "property_id":{"type":["integer","null"]},"property_name":{"type":"string"},"read":{"type":"string"},
            "write":{"type":"object","additionalProperties":false,"required":["write_allowed","api_writable","reason"],"properties":{"write_allowed":{"type":"boolean"},"api_writable":{"type":["boolean","null"]},"reason":{"type":["string","null"]}}},
            "obsolete":{"type":"boolean"},"definition":{},"source":{"type":"string"},"version":{"type":"string"},"runtime_differences":{"type":["array","null"],"items":{"type":"string"}}}}}}}
            """)!;
        JsonNode item = output["properties"]!["items"]!["items"]!["properties"]!;
        string definition = PropertySchemaTools.MetadataJson.GetJsonSchemaAsNode(typeof(PropertyDefinitionMetadata)).ToJsonString();
        item["definition"] = JsonNode.Parse(definition.Replace("\"#/", "\"#/properties/items/items/properties/definition/", StringComparison.Ordinal));
        item["read"]!["enum"] = new JsonArray([.. PropertyCatalog.Entries.Select(e => e.ReadRefusal ?? "readable")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(n => (JsonNode?)JsonValue.Create(n))]);
        tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(output);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Post-processing of the schemas the SDK generates. The SDK omits <c>additionalProperties: false</c> (spike S4,
/// api-notes C), which mcp-tool-design 5 requires so a misspelled argument is rejected instead of ignored.
/// </summary>
internal static class ToolSchemas
{
    public static void Harden(IEnumerable<McpServerTool> tools)
    {
        foreach (McpServerTool tool in tools)
        {
            if (JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) is JsonObject schema
                && schema["type"]?.GetValue<string>() == "object"
                && !schema.ContainsKey("additionalProperties"))
            {
                schema["additionalProperties"] = false;
                schema["properties"] ??= new JsonObject();
                if (tool.ProtocolTool.Name == ListImportTools.ImportTool)
                {
                    schema["allOf"] = JsonNode.Parse("""[{"if":{"properties":{"kind":{"const":"parts"}}},"then":{"properties":{"format":{"const":"xml"}}}}]""");
                    var output = JsonNode.Parse(tool.ProtocolTool.OutputSchema!.Value.GetRawText())!.AsObject();
                    RequireProperties(output);
                    tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(output);
                }

                // Objects inside array arguments too (eplan_page_create's pages), which UnknownArgumentFilter checks.
                foreach (JsonObject items in schema["properties"]!.AsObject().Select(p => (p.Value as JsonObject)?["items"]).OfType<JsonObject>())
                {
                    if (items["type"] is JsonValue type && type.TryGetValue(out string? kind) && kind == "object" && !items.ContainsKey("additionalProperties"))
                    {
                        items["additionalProperties"] = false;
                    }
                }

                tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
            }
        }
    }

    private static void RequireProperties(JsonObject schema)
    {
        if (schema["properties"] is JsonObject properties)
        {
            schema["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]);
            schema["additionalProperties"] = false;
            foreach ((string name, JsonNode? child) in properties)
            {
                if (child is JsonObject field)
                {
                    if (name is "deviceListEntries" or "projectParts" or "articleReferences")
                    {
                        field["minimum"] = 0;
                    }

                    if (name == "level")
                    {
                        field["enum"] = new JsonArray("message", "warning", "error", "fatal");
                    }

                    RequireProperties(field);
                }
            }
        }

        if (schema["items"] is JsonObject items)
        {
            RequireProperties(items);
        }
    }
}

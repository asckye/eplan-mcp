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
}

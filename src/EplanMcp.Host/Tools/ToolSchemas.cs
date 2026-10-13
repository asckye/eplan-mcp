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
                tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
            }
        }
    }
}

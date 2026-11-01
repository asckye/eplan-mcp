using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
namespace EplanMcp.Host.Tools;

internal static class ApiTrySchema
{
    internal static void Configure(McpServerOptions options, bool enabled)
    {
        McpServerTool? tool = options.ToolCollection?.FirstOrDefault(t => t.ProtocolTool.Name == ApiTryTools.Tool);
        if (tool is null)
        { return; }
        JsonNode input = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        input["properties"]!["parameters"] = JsonNode.Parse("""{"type":"object","additionalProperties":{"oneOf":[{"type":"string"},{"type":"number"},{"type":"boolean"},{"type":"object","additionalProperties":false,"required":["x","y"],"properties":{"x":{"type":"number"},"y":{"type":"number"}}},{"type":"object","additionalProperties":false,"required":["project","page","type","oid","digest"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"type":{"type":"integer","minimum":1,"maximum":2147483647},"oid":{"type":"integer","minimum":1},"digest":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}]}}""");
        var handle = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["handle","type"],"properties":{"handle":{"type":"string","pattern":"^h:[0-9a-f]{32}$"},"type":{"type":"string"}}}""")!;
        input["properties"]!["parameters"]!["additionalProperties"]!["oneOf"]!.AsArray().Add(handle.DeepClone());
        JsonNode output = JsonNode.Parse(tool.ProtocolTool.OutputSchema!.Value.GetRawText())!;
        output["properties"]!["value"] = new JsonObject { ["oneOf"] = new JsonArray(JsonNode.Parse("""{"type":"string"}"""), input["properties"]!["parameters"]!["additionalProperties"]!["oneOf"]![4]!.DeepClone(), handle.DeepClone(), JsonNode.Parse("""{"type":"null"}""")) };
        tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(output);
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(input);
        if (enabled)
        { return; }
        options.ToolCollection!.Remove(tool);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
namespace EplanMcp.Host.Tools;
internal static class ApiTrySchema
{
    internal static void Configure(McpServerOptions options, bool enabled)
    {
        McpServerTool? tool = options.ToolCollection?.SingleOrDefault(t => t.ProtocolTool.Name == ApiTryTools.Tool);
        if (tool is null) { return; }
        JsonNode input = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        input["properties"]!["parameters"] = JsonNode.Parse("""{"type":"object","additionalProperties":{"oneOf":[{"type":"string"},{"type":"number"},{"type":"boolean"},{"type":"object","additionalProperties":false,"required":["project","page","type","oid","digest"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"type":{"type":"integer","minimum":1,"maximum":2147483647},"oid":{"type":"integer","minimum":1},"digest":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}]}}""");
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(input);
        if (enabled) { return; }
        options.ToolCollection!.Remove(tool);
        var usage = options.ToolCollection.Single(t => t.ProtocolTool.Name == UsageTools.UsageTool).ProtocolTool;
        JsonNode schema = JsonNode.Parse(usage.InputSchema.GetRawText())!;
        JsonArray names = schema["properties"]!["tool"]!["enum"]!.AsArray();
        // The usage tool is shared by every HTTP session; a later session finds the name already gone.
        foreach (JsonNode? name in names.Where(n => n!.GetValue<string>() == ApiTryTools.Tool).ToList()) { names.Remove(name); }
        usage.InputSchema = JsonSerializer.SerializeToElement(schema);
    }
}

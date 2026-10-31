using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

internal static class ActionTrySchema
{
    public static void Configure(McpServerOptions options, bool enabled)
    {
        foreach (McpServerTool tool in (options.ToolCollection ?? []).Where(t => t.ProtocolTool.Name == ActionTryTools.Tool).ToList())
        {
            if (!enabled)
            { options.ToolCollection!.Remove(tool); }
            else
            {
                var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
                schema["properties"]!["parameters"] = JsonNode.Parse("""{"type":"object","maxProperties":32,"additionalProperties":{"type":["string","number","boolean"]}}""");
                tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
                tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["action","type","project","unverified","execution","dryRun","recovered","before","after","changed","truncated","hint"],"properties":{"action":{"type":"string"},"type":{"type":["string","null"]},"project":{"type":"string"},"unverified":{"const":true},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"recovered":{"type":"boolean"},"before":{"type":"object","additionalProperties":{"type":"string"}},"after":{"type":["object","null"],"additionalProperties":{"type":"string"}},"changed":{"type":["boolean","null"]},"truncated":{"type":"boolean"},"hint":{"type":"string"}}}"""));
            }
        }
        if (enabled)
        { return; }
        foreach (McpServerTool usage in (options.ToolCollection ?? []).Where(t => t.ProtocolTool.Name == UsageTools.UsageTool).ToList())
        {
            var schema = JsonNode.Parse(usage.ProtocolTool.InputSchema.GetRawText())!;
            var names = schema["properties"]!["tool"]!["enum"]!.AsArray();
            foreach (JsonNode? name in names.Where(n => n?.GetValue<string>() == ActionTryTools.Tool).ToList())
            { names.Remove(name); }
            usage.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
        }
    }
}

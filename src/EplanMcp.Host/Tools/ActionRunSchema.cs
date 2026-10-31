using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Safety;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Compact discovery contract; entry-specific rules live in Host validation and usage.</summary>
internal static class ActionRunSchema
{
    public static void Configure(McpServerOptions options, HostConfig config)
    {
        McpServerTool? tool = options.ToolCollection?.FirstOrDefault(t => t.ProtocolTool.Name == ActionRunTools.Tool);
        if (tool is null)
        {
            return;
        }
        var usage = options.ToolCollection!.First(t => t.ProtocolTool.Name == UsageTools.UsageTool).ProtocolTool;
        var usageSchema = JsonNode.Parse(usage.InputSchema.GetRawText())!;
        if (config.ActionRun == ActionRunSwitch.Off)
        {
            options.ToolCollection!.Remove(tool);
        }
        else
        {
            var schema = JsonNode.Parse("""{"type":"object","properties":{"entry":{},"project":{"type":"string"},"arguments":{"type":"object","additionalProperties":{"type":"string"}},"dry_run":{"type":"boolean","default":false}},"required":["entry","arguments"],"additionalProperties":false}""")!;
            schema["properties"]!["entry"]!["enum"] = JsonSerializer.SerializeToNode(ActionRunCatalog.Entries.Keys);
            tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
            using Stream output = typeof(ActionRunSchema).Assembly.GetManifestResourceStream("EplanMcp.Host.Tools.ActionRun.output.json")!;
            tool.ProtocolTool.OutputSchema = JsonSerializer.Deserialize<JsonElement>(output);
            usageSchema["properties"]!["topic"] = new JsonObject { ["type"] = "string" };
            usageSchema["allOf"] = JsonNode.Parse("""[{"if":{"properties":{"tool":{"const":"eplan_action_run"}}},"else":{"properties":{"topic":{"enum":["examples","workflow","fields","errors"]}}}}]""");
        }
        usage.InputSchema = JsonSerializer.SerializeToElement(usageSchema);
    }
}

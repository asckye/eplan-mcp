using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The meta tool of mcp-tool-design 10: detailed usage on demand keeps every tool description within its budget.
/// Answers from <see cref="ToolUsageCatalog"/> only; never calls EPLAN.
/// </summary>
[McpServerToolType]
internal sealed class UsageTools
{
    public const string UsageTool = "eplan_tool_usage";

    public static void Configure(McpServerOptions options)
    {
        // HTTP sessions share tool definitions. Rebuild the enum from the filtered collection on every configuration.
        string[] names = [.. (options.ToolCollection ?? []).Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal)];
        foreach (McpServerTool usage in (options.ToolCollection ?? []).Where(t => t.ProtocolTool.Name == UsageTool))
        {
            var schema = JsonNode.Parse(usage.ProtocolTool.InputSchema.GetRawText())!;
            schema["properties"]!["tool"]!["enum"] = JsonSerializer.SerializeToNode(names);
            usage.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
        }
    }

    [McpServerTool(
        Name = UsageTool,
        Title = "Detailed usage of an EplanMcp tool",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolUsageResult))]
    [Description(
        "Returns the detailed usage of one EplanMcp tool: argument examples, the call order with other tools, the result fields " +
        "and what to do about each error. Use it when a description leaves a question open. Works without EPLAN. Example: " +
        "{\"tool\": \"eplan_project_list\", \"topic\": \"errors\"}.")]
    public static CallToolResult ToolUsage(
        IOptions<McpServerOptions> options,
        [Description("The tool to explain.")] string tool,
        [Description("Only this section; omit for all.")]
        [AllowedValues(ToolUsageCatalog.Examples, ToolUsageCatalog.Workflow, ToolUsageCatalog.Fields, ToolUsageCatalog.Errors)]
        string? topic = null)
    {
        string[] names = [.. (options.Value.ToolCollection ?? []).Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal)];
        bool registered = names.Contains(tool, StringComparer.Ordinal);
        if (tool == ActionRunTools.Tool)
        {
            return registered
                ? ActionRunUsage.For(topic)
                : ToolResults.Error(ActionRunCatalog.Denied("actionRun is unavailable in this configuration/profile.").Error);
        }
        var violations = new List<string>();
        string name = tool ?? string.Empty; // an explicit JSON null arrives as null despite the type
        IReadOnlyDictionary<string, string>? sections = registered ? ToolUsageCatalog.Usage.GetValueOrDefault(name) : null;
        if (sections is null)
        {
            violations.Add($"'tool' must be one of: {string.Join(", ", names)}.");
        }

        if (topic is not null && !ToolUsageCatalog.Topics.Contains(topic, StringComparer.Ordinal))
        {
            violations.Add($"'topic' must be one of: {string.Join(", ", ToolUsageCatalog.Topics)}; omit it for all four.");
        }

        if (sections is null || violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        string[] topics = topic is null ? [.. ToolUsageCatalog.Topics] : [topic];
        var result = new ToolUsageResult
        {
            Tool = name,
            Topic = topic,
            Text = string.Join("\n\n", topics.Select(t => sections[t])),
        };
        return ToolResults.Success($"Usage of {name}: {string.Join(", ", topics)}.", result);
    }
}

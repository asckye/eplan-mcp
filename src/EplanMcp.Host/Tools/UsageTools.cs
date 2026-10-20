using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
        // The enum lists every registered tool; ToolContractTests fails when it, the usage files and the tools differ.
        [Description("The tool to explain.")][AllowedValues(SessionTools.StatusTool, SessionTools.SelectTool, ProjectTools.ListTool, ProjectTools.GetTool, PageTools.ListTool, PageTools.GetTool, PageWriteTools.CreateTool, PageWriteTools.UpdateTool, PageDestructiveTools.DeleteTool, DeviceTools.ListTool, DeviceTools.GetTool, DeviceWriteTools.UpdateTool, ConnectionTools.ListTool, ConnectionWriteTools.GenerateTool, PartTools.ListTool, MessageTools.ListTool, CheckTools.ListTool, CheckWriteTools.RunTool, MacroTools.ListTool, MacroTools.GetTool, MacroWriteTools.InsertTool, ExportTools.PdfTool, ExportTools.DxfTool, ImageExportTools.ImageTool, ReportWriteTools.UpdateTool, UsageTool)] string tool,
        [Description("Only this section; omit for all.")]
        [AllowedValues(ToolUsageCatalog.Examples, ToolUsageCatalog.Workflow, ToolUsageCatalog.Fields, ToolUsageCatalog.Errors)]
        string? topic = null)
    {
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> usage = ToolUsageCatalog.Usage;
        var violations = new List<string>();
        string name = tool ?? string.Empty; // an explicit JSON null arrives as null despite the type
        IReadOnlyDictionary<string, string>? sections = usage.GetValueOrDefault(name);
        if (sections is null)
        {
            violations.Add($"'tool' must be one of: {string.Join(", ", usage.Keys.Order(StringComparer.Ordinal))}.");
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

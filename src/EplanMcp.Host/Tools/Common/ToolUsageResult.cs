using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_tool_usage</c> (docs/tools/eplan_tool_usage.md).</summary>
internal sealed class ToolUsageResult
{
    [Description("The tool the text explains.")]
    public string Tool { get; set; } = string.Empty;

    [Description("The section returned; null when all four are returned.")]
    public string? Topic { get; set; }

    [Description("Usage text in Markdown, one '## ' heading per section. Written by EplanMcp; contains no project data.")]
    public string Text { get; set; } = string.Empty;
}

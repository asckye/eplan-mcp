using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Which tools <c>serve</c> offers (mcp-tool-design 10, M4c design 4.2).</summary>
internal enum ToolProfile
{
    /// <summary>Every tool; the default.</summary>
    Full,

    /// <summary>At most <see cref="ToolProfiles.CoreLimit"/> tools, for clients that cap how many tools they take.</summary>
    Core,
}

/// <summary>The profile <c>serve</c> was started with; tests and other hosts get <see cref="ToolProfile.Full"/>.</summary>
internal sealed record ToolProfileChoice(ToolProfile Profile);

/// <summary>
/// The tool profiles. Core is an explicit list: every Read and Write tool and the exports as of M4, so a new tool joins
/// full only until it is added here. Destructive tools are full only.
/// </summary>
internal static class ToolProfiles
{
    public const int CoreLimit = 25;

    public static readonly IReadOnlySet<string> Core = new HashSet<string>(StringComparer.Ordinal)
    {
        SessionTools.StatusTool, SessionTools.SelectTool, ProjectTools.ListTool, ProjectTools.GetTool, PageTools.ListTool,
        PageTools.GetTool, DeviceTools.ListTool, DeviceTools.GetTool, ConnectionTools.ListTool, PartTools.ListTool,
        MessageTools.ListTool, CheckTools.ListTool, MacroTools.ListTool, MacroTools.GetTool, UsageTools.UsageTool,
        PageWriteTools.CreateTool, PageWriteTools.UpdateTool, DeviceWriteTools.UpdateTool, MacroWriteTools.InsertTool,
        ConnectionWriteTools.GenerateTool, CheckWriteTools.RunTool, ReportWriteTools.UpdateTool, ExportTools.PdfTool,
        ExportTools.DxfTool, ImageExportTools.ImageTool,
    };

    /// <summary>
    /// Leaves only the profile's tools.
    /// </summary>
    public static void Apply(ICollection<McpServerTool> tools, ToolProfile profile)
    {
        if (profile == ToolProfile.Full)
        {
            return;
        }

        foreach (McpServerTool tool in tools.Where(t => !Core.Contains(t.ProtocolTool.Name)).ToList())
        {
            tools.Remove(tool);
        }
    }
}

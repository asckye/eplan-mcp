using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Which tools <c>serve</c> offers (the MCP server standard, M4c design 4.2).</summary>
internal enum ToolProfile
{
    /// <summary>Every tool; the default.</summary>
    Full,

    /// <summary>At most <see cref="ToolProfiles.CoreLimit"/> tools, for clients that cap how many tools they take.</summary>
    Core,

    /// <summary>
    /// Four tools: search, usage and two dispatchers that call every other tool, split by safety level so the client can
    /// still ask before writes (ADR-0057).
    /// </summary>
    Discovery,
}

/// <summary>The profile <c>serve</c> was started with; tests and other hosts get <see cref="ToolProfile.Full"/>.</summary>
internal sealed record ToolProfileChoice(ToolProfile Profile);

/// <summary>
/// The tool profiles. Core is an explicit list, so a new tool joins full only until it is added here; Destructive tools
/// are full only. Since 2026-11-17 core is chosen for the main task, drawing a schematic page from a description,
/// reviewing it and fixing it: read the project, find symbols and parts, create a page, place devices, macros, wires,
/// texts and graphics, move and retext them, generate connections, review, and look at the result. The M4 list
/// (reads, macros, checks, reports and the PDF export) could not place a symbol or draw a wire.
/// </summary>
internal static class ToolProfiles
{
    public const int CoreLimit = 25;

    public static readonly IReadOnlySet<string> Core = new HashSet<string>(StringComparer.Ordinal)
    {
        // Session and reading.
        SessionTools.StatusTool, SessionTools.SelectTool, ProjectTools.ListTool, ProjectTools.GetTool, PageTools.ListTool,
        PageTools.GetTool, DeviceTools.ListTool, DeviceTools.GetTool, ConnectionTools.ListTool, UsageTools.UsageTool,
        // Finding what to place.
        SymbolTools.Tool, PartTools.SearchTool, MacroTools.ListTool,
        // Drawing.
        PageWriteTools.CreateTool, PageWriteTools.UpdateTool, DeviceWriteTools.PlaceTool, MacroWriteTools.InsertTool,
        ConnectionCreateTools.Tool, PlacementCreateTools.Tool, ConnectionWriteTools.GenerateTool,
        // Fixing.
        // Fixing: move and retext. Deleting is Destructive (approval and backup even in scratch) and stays in full.
        PlacementUpdateTools.Tool, PropertySetTools.Tool, DeviceWriteTools.UpdateTool,
        // Reviewing and looking.
        PageReviewTools.Tool, ImageExportTools.ImageTool,
    };

    /// <summary>The discovery profile (ADR-0057); its three own tools are offered in no other profile.</summary>
    public static readonly IReadOnlySet<string> Discovery = new HashSet<string>(StringComparer.Ordinal)
    {
        DiscoveryTools.SearchTool, UsageTools.UsageTool, DiscoveryTools.ReadTool, DiscoveryTools.WriteTool,
    };

    /// <summary>
    /// Leaves only the profile's tools. Full keeps everything but the discovery tools, which would only repeat it.
    /// </summary>
    public static void Apply(ICollection<McpServerTool> tools, ToolProfile profile)
    {
        IReadOnlySet<string>? keep = profile switch { ToolProfile.Core => Core, ToolProfile.Discovery => Discovery, _ => null };
        foreach (McpServerTool tool in tools.Where(t => keep is null
            ? t.ProtocolTool.Name is DiscoveryTools.SearchTool or DiscoveryTools.ReadTool or DiscoveryTools.WriteTool
            : !keep.Contains(t.ProtocolTool.Name)).ToList())
        {
            tools.Remove(tool);
        }
    }
}

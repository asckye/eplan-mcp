using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The tools as they were before the profile removed any (ADR-0057): the switches (actionRun, actionTry, apiTry) have
/// already taken theirs, so a tool turned off is not here either. The discovery dispatchers call from this list, and
/// eplan_tool_usage explains <see cref="Explained"/>.
/// </summary>
internal sealed class ToolRegistry
{
    /// <summary>Every tool the switches left, before profile filtering.</summary>
    public IReadOnlyList<McpServerTool> All { get; private set; } = [];

    /// <summary>The names eplan_tool_usage accepts: the offered tools, and in discovery every tool a dispatcher reaches.</summary>
    public IReadOnlyList<string> Explained { get; private set; } = [];

    /// <summary>The tools a dispatcher may call: all but the four discovery tools themselves.</summary>
    public IEnumerable<McpServerTool> Forwardable => All.Where(t => !ToolProfiles.Discovery.Contains(t.ProtocolTool.Name));

    /// <summary>Applies <paramref name="profile"/> to <paramref name="tools"/> and keeps what it needs from before and after.</summary>
    public void Apply(ICollection<McpServerTool> tools, ToolProfile profile)
    {
        All = [.. tools];
        ToolProfiles.Apply(tools, profile);
        IEnumerable<McpServerTool> explained = profile == ToolProfile.Discovery ? All : tools;
        Explained = [.. explained.Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal)];
    }
}

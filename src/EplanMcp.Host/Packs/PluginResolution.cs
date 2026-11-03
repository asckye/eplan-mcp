using System.Text.Json;

namespace EplanMcp.Host.Packs;

/// <summary>Effective configuration items with winner, overridden sources and trace, plus the plugins used and data gaps.</summary>
internal sealed record PluginResolution(JsonElement Values, IReadOnlyList<string> Plugins, IReadOnlyList<string> Warnings, JsonElement Trace)
{
    public string? StateRevision { get; init; }
    public string? BindingProject { get; init; }
    public int? BindingRevision { get; init; }
    public IReadOnlyList<PluginDescriptor> Enabled { get; init; } = [];
}

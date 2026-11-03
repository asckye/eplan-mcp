using System.ComponentModel;

namespace EplanMcp.Host.Tools;

internal sealed record PluginEnableResult(string Plugin, string Version,
    [property: Description("Whether this exact version is globally enabled; null when state is unreadable.")] bool? Enabled,
    bool Changed, IReadOnlyList<string> Warnings);

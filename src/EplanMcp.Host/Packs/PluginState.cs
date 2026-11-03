namespace EplanMcp.Host.Packs;

internal sealed record PluginState(int Version, IReadOnlyList<PluginIdentity> Enabled, IReadOnlyList<RegisteredPluginLibrary> Libraries)
{
    public static PluginState Empty { get; } = new(1, [], []);
}

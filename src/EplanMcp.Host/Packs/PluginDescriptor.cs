using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>An enabled or locked plugin, including entries whose data could not be read.</summary>
internal sealed record PluginDescriptor(string Id, string Version, string Scope, PackManifest? Manifest, IReadOnlyList<string> Warnings);

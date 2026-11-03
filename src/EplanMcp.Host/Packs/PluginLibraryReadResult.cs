using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

internal sealed record PluginLibraryReadResult(string Name, string SourceType, string Folder,
    PluginLibraryIndex? Index, IReadOnlyList<string> Warnings)
{
    public bool Readable => Index is not null;
}

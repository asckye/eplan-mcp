using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs;

/// <summary>
/// One entry of the resource index (M6 design 4.1.3): a resource of a pack version with its asset name, its knowledge
/// document when it is one (or why that cannot be read), and its standing from the manifest's slots.
/// </summary>
public sealed record AssetEntry(string Pack, string Version, PackResource Resource, KnowledgeDocument? Document, string? DocumentProblem, KnowledgeStanding? Standing)
{
    /// <summary>The local part of the asset id: <c>&lt;kind&gt;/&lt;name&gt;</c>.</summary>
    public string Local { get; } = AssetNames.Local(Resource);

    public string Id => AssetNames.Id(Pack, Version, Local);

    /// <summary>The kind in snake_case, such as <c>macro</c> or <c>candidate</c>.</summary>
    public string Kind => AssetNames.KindOf(Local);

    public string Name => Local[(Kind.Length + 1)..];

    /// <summary>A knowledge document's tags, or a native resource's subtype as its one tag.</summary>
    public IReadOnlyList<string> Tags => Document?.Metadata.Tags ?? (Resource.Subtype is { } subtype ? [subtype] : []);
}

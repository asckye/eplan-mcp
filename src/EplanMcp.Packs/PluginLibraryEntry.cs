namespace EplanMcp.Packs;

/// <summary>An archive advertised by a library; Digest is its PackDigest, not its ZIP hash.</summary>
public sealed record PluginLibraryEntry
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public required PackKind Kind { get; init; }

    public required string Digest { get; init; }

    public required string File { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<string> Standards { get; init; } = [];
}

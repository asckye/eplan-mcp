namespace EplanMcp.Packs;

/// <summary>A library's data-only index; reading it never opens its archives.</summary>
public sealed record PluginLibraryIndex
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<PluginLibraryEntry> Plugins { get; init; }
}

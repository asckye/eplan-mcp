namespace EplanMcp.Packs.Scanning;

/// <summary>What the engineer names when building a pack; everything else in the manifest is generated.</summary>
public sealed record PackIdentity(
    string Id,
    string Name,
    string Version,
    string? Description = null,
    string? Author = null,
    string? EplanVersions = null,
    IReadOnlyList<string>? Variants = null);

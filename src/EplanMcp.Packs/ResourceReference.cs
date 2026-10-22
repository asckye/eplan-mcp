using System.ComponentModel;

namespace EplanMcp.Packs;

/// <summary>
/// Where a referenced resource lies (ADR-0024 section 6): a root that the engineer lists by name in config.json
/// (<c>packSources</c>, which may be a UNC path) and a path below it. Never an absolute path or an EPLAN variable such as
/// <c>$(MD_MACROS)</c>, so the manifest says nothing about a machine. TODO(M6): resolve the root through
/// <c>packSources</c> and verify the SHA-256 on each use (a mismatch makes the resource unavailable); v0 only records it.
/// </summary>
[Description("Where a referenced resource lies: the name of a root listed in config.json packSources, and the path below it. Each use verifies the SHA-256; a mismatch makes the resource unavailable.")]
public sealed record ResourceReference
{
    [Description("The root's name in config.json packSources.")]
    [Schema(Pattern = PackPatterns.Id)]
    public required string Root { get; init; }

    [Description("The path below the root, '/' between folders; no drive, no '..', no EPLAN variable.")]
    public required string Path { get; init; }
}

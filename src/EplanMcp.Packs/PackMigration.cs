using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("How to move to this version from older ones (M6 design 4.1.5): which versions, what changed.")]
public sealed record PackMigration
{
    [Description("The older versions this applies to, as a version range such as '^1.0.0'.")]
    public required string From { get; init; }

    public string? Notes { get; init; }

    [Description("What changed, one line each.")]
    public IReadOnlyList<string> Changes { get; init; } = [];
}

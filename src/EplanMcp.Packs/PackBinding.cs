using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("The pack versions one target is pinned to.")]
public sealed record PackBinding
{
    [Description("What the packs are bound to: an EPLAN project's link file path, or another name the engineer chooses. The file stays in EplanMcp's data folder, never in the project.")]
    public required string Target { get; init; }

    [Description("Sorted by id, one version per id.")]
    public IReadOnlyList<PackReference> Packs { get; init; } = [];
}

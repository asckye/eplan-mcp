using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("A file or folder given to build the pack.")]
public sealed record PackInput
{
    [Description("The last part of the path given, unique among the inputs; never the full local path.")]
    public required string Name { get; init; }

    public required PackInputKind Kind { get; init; }
}

using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("Configuration by scope, from the widest to the narrowest. Only the slots exist so far; what a scope may configure comes with the M6 design.")]
public sealed record PackConfiguration
{
    public PackScope? Company { get; init; }

    public IReadOnlyList<PackScope> CabinetTypes { get; init; } = [];

    public IReadOnlyList<PackScope> Projects { get; init; } = [];

    public IReadOnlyList<PackScope> Cabinets { get; init; } = [];
}

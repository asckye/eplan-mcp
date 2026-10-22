using System.ComponentModel;

namespace EplanMcp.Packs;

/// <summary>
/// The configuration layers a pack carries (M6 design 4.3): <c>base</c> (a base pack only), <c>company</c> and
/// <c>cabinetTypes</c>. Project and cabinet values belong to one EPLAN project and live in its workspace binding, never in
/// a pack. Only the slots exist so far; the layers' content comes with ConfigLayer documents.
/// </summary>
[Description("The configuration layers a pack carries, from the widest: base (a base pack only), company, cabinet types. Project and cabinet values live in the project's workspace binding, never in a pack.")]
public sealed record PackConfiguration
{
    public PackScope? Base { get; init; }

    public PackScope? Company { get; init; }

    public IReadOnlyList<PackScope> CabinetTypes { get; init; } = [];
}

using System.ComponentModel;

namespace EplanMcp.Packs;

/// <summary>
/// <c>workspaces\&lt;project&gt;\binding.json</c>: one EPLAN project's binding (M6 design 4.3, 4.1.5), kept in EplanMcp's
/// data folder and never in the project. Project and cabinet values live here, not in packs.
/// </summary>
[Description("One EPLAN project's binding (workspaces\\<project>\\binding.json): its cabinets, the pack versions it locks by digest, and a revision that grows with every change. Kept in EplanMcp's data folder, never in the project.")]
public sealed record ProjectBinding
{
    public const int CurrentBindingVersion = 1;

    public const string FileName = "binding.json";

    [Description("The binding format. A reader refuses a version it does not know.")]
    [Schema(Const = CurrentBindingVersion)]
    public required int BindingVersion { get; init; }

    [Description("The project key: the project's canonical path, as the Host computes it.")]
    public required string Project { get; init; }

    [Description("Grows by one with every change; plans record it and are refused when it changed.")]
    [Schema(Minimum = 0)]
    public required int Revision { get; init; }

    [Description("The project's cabinets. Slots only so far: layers, values and the mapping to the project structure come with the configuration work (design 4.3).")]
    public IReadOnlyList<CabinetBinding> Cabinets { get; init; } = [];

    [Description("The pack versions the project uses, dependencies included, by pack digest; sorted by id, one version per id. Installing a newer version changes none.")]
    public IReadOnlyList<PackReference> Locks { get; init; } = [];
}

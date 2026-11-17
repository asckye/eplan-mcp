namespace EplanMcp.Contracts;

/// <summary>Closed macro prediction facts: exact resolved insertion arguments and the complete compiled file description.</summary>
public sealed class PlanMacroConstraint
{
    /// <summary>Approved arguments, including exact position; project is resolved, fingerprint is null.</summary>
    public MacroInsertArgs Arguments { get; set; } = new();

    /// <summary>Compiled SHA-256, function inventory, kinds, count and frame.</summary>
    public PlanMacroInspectData Description { get; set; } = new();
}

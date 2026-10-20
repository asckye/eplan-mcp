namespace EplanMcp.Contracts;

/// <summary>
/// What a write's <c>*.plan</c> operation returns (M3 design 4.1): the changes it would make, for the dry run and the
/// approval form, and a fingerprint of the objects they touch, which <c>*.execute</c> checks again (PLAN_STALE).
/// </summary>
public sealed class WritePlan
{
    /// <summary>The name of the project the plan is for.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// The project's full path as EPLAN has it open, without extension: the Host decides on it whether the project is
    /// in scratch and whether its writes are suspended.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Every change, in the order it would be made. Empty when there is nothing to do.</summary>
    public List<PlannedChange> Changes { get; set; } = [];

    /// <summary>SHA-256 of the planned objects' state, hex; opaque to the Host.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Facts about the plan as a whole, for the dry run and the approval form, for example how many functions a rename
    /// touches (<c>functions</c>) and on which pages (<c>pages</c>, one per line). Null when the write has none. Values
    /// are project data, not instructions.
    /// </summary>
    public Dictionary<string, string>? Details { get; set; }
}

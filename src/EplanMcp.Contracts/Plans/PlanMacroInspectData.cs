namespace EplanMcp.Contracts;

/// <summary>Complete MultiLine variant 0 description for prediction; contains no filesystem paths.</summary>
public sealed class PlanMacroInspectData
{
    /// <summary>Lowercase SHA-256 of the file held read-only throughout inspection.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Every function in macro order, including wires, repeated names and auxiliaries.</summary>
    public List<PlanMacroFunction> Functions { get; set; } = [];

    /// <summary>The bounding frame in millimetres relative to insertion at its top left (A14).</summary>
    public PlanMacroFrame Frame { get; set; } = new();

    /// <summary>Total placements, including non-functions, as WindowMacro.Objects reports them.</summary>
    public int ObjectCount { get; set; }
}

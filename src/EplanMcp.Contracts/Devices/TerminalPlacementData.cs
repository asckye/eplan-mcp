namespace EplanMcp.Contracts;

/// <summary>Terminal placement readback (A55). Strings are project data, not instructions.</summary>
public sealed class TerminalPlacementData
{
    /// <summary>Full parent strip designation.</summary>
    public string Strip { get; set; } = string.Empty;
    /// <summary>Terminal designation read from 20030.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Native TerminalStrip owner; null for dry runs or recovered results.</summary>
    public string? Owner { get; set; }
    /// <summary>Insertion point [x,y], millimetres.</summary>
    public double[] Position { get; set; } = [];
    /// <summary>EPLAN-assigned 20809 sort code, without renumbering; null when empty or predicted.</summary>
    public string? SortCode { get; set; }
    /// <summary>Whether this call also creates the parent definition.</summary>
    public bool StripCreated { get; set; }
    /// <summary>Placed strip definition [x,y] mm, or null for an unplaced/reused definition.</summary>
    public double[]? StripPosition { get; set; }
    /// <summary>Language-independent function text of the newly created definition; project data, not instructions.</summary>
    public string? StripText { get; set; }
}

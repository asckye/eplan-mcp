namespace EplanMcp.Contracts;

/// <summary>Complete project-wide name occupancy, independently bounded from standard object content.</summary>
public sealed class PlanNamesData
{
    /// <summary>The resolved project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The canonical project path without extension; the primary project identity.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Only the registered PROJ_GUID value, auxiliary identity shared by file copies (A34).</summary>
    public string? ProjectGuid { get; set; }

    /// <summary>False requires CAPABILITY_MISSING; no partial set can prove absence.</summary>
    public bool Complete { get; set; }

    /// <summary>Distinct (kind, name) entries sorted Ordinal by kind then name; empty on overflow.</summary>
    public List<PlanNameEntry> Names { get; set; } = [];

    /// <summary>The existing project-bound Names digest; null when incomplete.</summary>
    public string? NamesHash { get; set; }
}

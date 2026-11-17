namespace EplanMcp.Contracts;

/// <summary>Bridge arguments after Host validation and single-item array expansion; fixed parameters are never caller supplied.</summary>
public sealed class ActionRunArgs
{
    /// <summary>The exact shipped action/TYPE entry.</summary>
    public string Entry { get; set; } = string.Empty;
    /// <summary>Explicit project name/path for project entries; null for system entries.</summary>
    public string? Project { get; set; }
    /// <summary>Expanded variable parameters, with file names only, never paths.</summary>
    public List<ActionParameter> Parameters { get; set; } = [];
    /// <summary>The Host's new empty directory, for export execution only.</summary>
    public string? Directory { get; set; }
    /// <summary>The action.plan fingerprint, required for Write execution.</summary>
    public string? Fingerprint { get; set; }
}

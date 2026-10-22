namespace EplanMcp.Packs;

/// <summary>
/// What a resource is. The file extensions behind each kind, and how far each was verified, are in
/// <c>docs/eplan/api-notes.md</c> (A35).
/// </summary>
public enum ResourceKind
{
    /// <summary><c>.zw9</c>.</summary>
    BasicProject,
    /// <summary><c>.zw1</c>.</summary>
    ProjectBackup,
    /// <summary>A link file (<c>.elk</c>, <c>.ell</c>, <c>.elr</c>) with its <c>.edb</c> data folder.</summary>
    Project,
    /// <summary>A window, symbol or page macro.</summary>
    Macro,
    /// <summary>A form (report template).</summary>
    Form,
    /// <summary>A plot frame.</summary>
    Frame,
    /// <summary>A company material mapping (CSV, XLSX, JSON).</summary>
    MaterialMapping,
    Document,
    Rule,
    Other,
}

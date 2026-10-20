namespace EplanMcp.Contracts;

/// <summary>
/// Result data of the export plans (<c>export.*.plan</c>): what the export will be, found without exporting, so that
/// the Host creates a folder only for an export that can run.
/// </summary>
public sealed class ExportPlanData
{
    /// <summary>The name of the project, which also names the export's folder.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>How many pages the export has.</summary>
    public int PageCount { get; set; }

    /// <summary>PDF, DXF: the scheme used, the given one or else the one EPLAN used last; null for images.</summary>
    public string? Scheme { get; set; }
}

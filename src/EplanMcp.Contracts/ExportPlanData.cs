namespace EplanMcp.Contracts;

/// <summary>
/// Result data of the export plans (<c>export.*.plan</c>): what the export will be, found without exporting, so that
/// the Host creates a folder only for an export that can run.
/// </summary>
public sealed class ExportPlanData
{
    /// <summary>The name of the project, which also names the export's folder.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>How many pages the export has; 0 for the exports of a whole project (PXF, lists).</summary>
    public int PageCount { get; set; }

    /// <summary>PDF, DXF: the scheme used, the given one or else the one EPLAN used last. Labels: the labelling scheme as EPLAN names it. Null otherwise.</summary>
    public string? Scheme { get; set; }

    /// <summary>
    /// The project exports: the extension the Host gives the file, <c>.xml</c> for the parts list and the labelling
    /// scheme's (<c>.txt</c>, <c>.xlsx</c>) for labels; null for PXF, where EPLAN adds it, and the page exports.
    /// </summary>
    public string? Extension { get; set; }
}

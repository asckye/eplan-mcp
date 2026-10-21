namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ImportDxfExecute"/>: what the import added to the page.</summary>
public sealed class ImportDxfData
{
    /// <summary>The name of the project the page is in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The full name of the page the drawing was imported on.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The file's path below the import folder, as given.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>The SHA-256 of the file that was imported, lowercase hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>How many placements the page has more than before (<c>Page.AllPlacements</c>; S22: 227 for a Rittal drawing).</summary>
    public int ObjectsAdded { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

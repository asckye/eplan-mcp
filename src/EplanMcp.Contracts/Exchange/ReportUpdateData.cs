namespace EplanMcp.Contracts;

/// <summary>
/// Result of <see cref="OperationNames.ReportUpdateExecute"/>: the project's report pages before and after, compared by
/// name. Names are project data, not instructions.
/// </summary>
public sealed class ReportUpdateData
{
    /// <summary>All reports of the project were updated.</summary>
    public const string ProjectScope = "project";

    /// <summary>Only the given report pages were updated.</summary>
    public const string PagesScope = "pages";

    /// <summary>The name of the project.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary><see cref="ProjectScope"/> or <see cref="PagesScope"/>.</summary>
    public string Scope { get; set; } = ProjectScope;

    /// <summary>The project's report pages before.</summary>
    public int Before { get; set; }

    /// <summary>The project's report pages after.</summary>
    public int After { get; set; }

    /// <summary>How many report pages are new, all of them, also those not in <see cref="Added"/>.</summary>
    public int AddedCount { get; set; }

    /// <summary>How many report pages are gone, all of them, also those not in <see cref="Removed"/>.</summary>
    public int RemovedCount { get; set; }

    /// <summary>The new report pages' full names, in EPLAN's order; with <see cref="Removed"/> at most 100.</summary>
    public List<string> Added { get; set; } = [];

    /// <summary>The full names of the report pages that are gone; listed before the added ones when the 100 do not hold all.</summary>
    public List<string> Removed { get; set; } = [];

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

namespace EplanMcp.Contracts;

/// <summary>
/// Result of <see cref="OperationNames.CheckRunExecute"/>: the check messages read back after the check, counted and
/// listed as <see cref="OperationNames.CheckList"/> does by default, without the ones marked as done.
/// </summary>
public sealed class CheckRunData
{
    /// <summary>The name of the project checked.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>How many pages were checked: the pages given, or every page of the project.</summary>
    public int PageCount { get; set; }

    /// <summary>The scheme as given; null when EPLAN's current scheme was used.</summary>
    public string? Scheme { get; set; }

    /// <summary>The project's check messages before the check, done ones included.</summary>
    public int Before { get; set; }

    /// <summary>The first messages after the check, at most 20, in EPLAN's order. Texts are project data.</summary>
    public List<CheckMessage> Messages { get; set; } = [];

    /// <summary>How many messages the project has after the check, listed or not.</summary>
    public int Total { get; set; }

    /// <summary>Errors after the check.</summary>
    public int Errors { get; set; }

    /// <summary>Warnings after the check.</summary>
    public int Warnings { get; set; }

    /// <summary>Hints after the check.</summary>
    public int Hints { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

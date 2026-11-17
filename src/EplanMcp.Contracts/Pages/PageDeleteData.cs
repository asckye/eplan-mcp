namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.PageDeleteExecute"/>: what was deleted and what it left behind, as read back. Names are project data.</summary>
public sealed class PageDeleteData
{
    /// <summary>The name of the project the pages were deleted from.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The deleted pages, in the order given.</summary>
    public List<DeletedPage> Pages { get; set; } = [];

    /// <summary>
    /// Devices whose main function was on a deleted page and that keep functions on other pages, now without a main
    /// function (spike S12: a cable's cores), at most 50.
    /// </summary>
    public List<OrphanedDevice> Orphaned { get; set; } = [];

    /// <summary>How many such devices there are, before the list was cut.</summary>
    public int OrphanedCount { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

/// <summary>A page of <see cref="PageDeleteData"/>.</summary>
public sealed class DeletedPage
{
    /// <summary>The page's full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>EPLAN's page type, e.g. <c>Circuit</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>How many functions were placed on the page.</summary>
    public int Functions { get; set; }
}


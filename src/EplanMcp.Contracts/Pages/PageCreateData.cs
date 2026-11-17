namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.PageCreateExecute"/>: the committed pages as read back from EPLAN.</summary>
public sealed class PageCreateData
{
    /// <summary>The name of the project the pages were created in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Each new page's name, type and description as EPLAN reads them back, in the order created.</summary>
    public List<PageSummary> Pages { get; set; } = [];

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.PageRenumberExecute"/>: the pages' names as read back. Names are project data.</summary>
public sealed class PageRenumberData
{
    /// <summary>The name of the project the pages are in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The pages whose name changed, in the order given.</summary>
    public List<RenamedObject> Changed { get; set; } = [];

    /// <summary>How many of the pages kept their name.</summary>
    public int Unchanged { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

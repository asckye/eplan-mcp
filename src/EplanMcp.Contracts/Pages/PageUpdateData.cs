namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.PageUpdateExecute"/>: the changed descriptions as read back from EPLAN.</summary>
public sealed class PageUpdateData
{
    /// <summary>The name of the project the pages are in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The language that was changed, for example <c>zh_CN</c>.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The pages whose description changed, in the order given.</summary>
    public List<PageDescriptionChange> Pages { get; set; } = [];

    /// <summary>Pages that already had the requested description and were left alone.</summary>
    public int Unchanged { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

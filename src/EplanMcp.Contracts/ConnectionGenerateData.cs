namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ConnectionGenerateExecute"/>: each page's connections before and after, read back.</summary>
public sealed class ConnectionGenerateData
{
    /// <summary>The name of the project the pages are in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The pages in the order given.</summary>
    public List<ConnectionGeneratePage> Pages { get; set; } = [];

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

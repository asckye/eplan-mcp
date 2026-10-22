namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.PlaceholderList"/>: a page of a project's placeholder objects.</summary>
public sealed class PlaceholderListData
{
    /// <summary>The name of the project actually used, so the tool can echo it (mcp-tool-design 7.1).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The page the list is limited to, as EPLAN names it; null for the whole project.</summary>
    public string? Page { get; set; }

    /// <summary>The requested slice, by page name, then name, then EPLAN's order.</summary>
    public List<PlaceholderItem> Placeholders { get; set; } = [];

    /// <summary>How many placeholders the project (or the page) has.</summary>
    public int Total { get; set; }
}

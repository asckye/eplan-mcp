namespace EplanMcp.Contracts;

/// <summary>
/// Common arguments of the M2 list operations (M2 design 4.3). The Bridge filters, sorts and slices in one main-thread
/// call; each operation adds its own filter fields. The Host has already checked the tool's limit range.
/// </summary>
public abstract class ListArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; null means the project selected in EPLAN.</summary>
    public string? Project { get; set; }

    /// <summary>Index of the first item to return, taken from the Host's cursor.</summary>
    public int Offset { get; set; }

    /// <summary>The most items to return.</summary>
    public int Limit { get; set; }
}

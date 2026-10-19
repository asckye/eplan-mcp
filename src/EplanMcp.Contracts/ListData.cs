namespace EplanMcp.Contracts;

/// <summary>Common result data of the M2 list operations (M2 design 4.3).</summary>
public sealed class ListData<TItem>
{
    /// <summary>The name of the project actually used, so the tool can echo it (mcp-tool-design 7.1).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The requested slice, in a stable order.</summary>
    public List<TItem> Items { get; set; } = [];

    /// <summary>How many items match the filters before slicing.</summary>
    public int Total { get; set; }
}

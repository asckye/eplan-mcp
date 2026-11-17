namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.MacroList"/>.</summary>
public sealed class MacroListData
{
    /// <summary>The requested slice, ordered by path (ordinal, ignoring case).</summary>
    public List<MacroListItem> Items { get; set; } = [];

    /// <summary>How many macros match, before slicing.</summary>
    public int Total { get; set; }

    /// <summary>
    /// True when the scan of the macro directory stopped at its time limit, so macros may be missing
    /// (docs/tools/eplan_macro_list.md, section 5).
    /// </summary>
    public bool Incomplete { get; set; }
}

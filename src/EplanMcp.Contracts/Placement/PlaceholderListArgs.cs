namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PlaceholderList"/> (docs/tools/eplan_placeholder_list.md); the paging applies to the placeholders.</summary>
public sealed class PlaceholderListArgs : ListArgs
{
    /// <summary>Only the placeholders on this page, a full page name found as eplan_page_get finds one; null for the whole project.</summary>
    public string? Page { get; set; }

    /// <summary>Whether to return each value set's values and the current values (<c>response_format: detailed</c>).</summary>
    public bool Detailed { get; set; }

    /// <summary>Language of the values, for example <c>zh_CN</c>; null means EPLAN's user interface language with fallback.</summary>
    public string? Language { get; set; }
}

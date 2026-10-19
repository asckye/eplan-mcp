namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PageGet"/>.</summary>
public sealed class PageGetArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; null means the project selected in EPLAN.</summary>
    public string? Project { get; set; }

    /// <summary>The full page name, for example <c>=GAA+A1&amp;EFS1/1</c>; matched exactly, or ignoring case when that is unique.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>Language of multi-language texts, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }

    /// <summary>Whether to read every non-empty page property (<c>response_format: "detailed"</c>).</summary>
    public bool IncludeProperties { get; set; }
}

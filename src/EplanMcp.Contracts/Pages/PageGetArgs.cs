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

    /// <summary>
    /// page.drawing.get only: the first placement to return, in the order parents first. With <see cref="DrawingLimit"/> the
    /// Bridge returns that slice, the page total and the fingerprint of every placement; without it, every placement.
    /// A146, Live 2026-11-16: model view pages of the sample project passed the 16 MiB reply limit when sent whole.
    /// </summary>
    public int? DrawingOffset { get; set; }

    /// <summary>page.drawing.get only: how many placements to return, 1 to 200; null returns every placement.</summary>
    public int? DrawingLimit { get; set; }
}

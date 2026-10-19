namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ProjectGet"/>.</summary>
public sealed class ProjectGetArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; null means the project selected in EPLAN.</summary>
    public string? Project { get; set; }

    /// <summary>Language of multi-language texts, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }

    /// <summary>Whether to read every non-empty project property (<c>response_format: "detailed"</c>).</summary>
    public bool IncludeProperties { get; set; }
}

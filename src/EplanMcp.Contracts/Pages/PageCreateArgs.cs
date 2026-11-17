namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PageCreatePlan"/> and <see cref="OperationNames.PageCreateExecute"/>.</summary>
public sealed class PageCreateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The pages to create, in this order: 1 to 50.</summary>
    public List<PageCreateItem> Pages { get; set; } = [];

    /// <summary>Language of the page descriptions, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}

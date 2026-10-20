namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PageUpdatePlan"/> and <see cref="OperationNames.PageUpdateExecute"/>.</summary>
public sealed class PageUpdateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The pages to change: 1 to 50.</summary>
    public List<PageUpdateItem> Pages { get; set; } = [];

    /// <summary>The one language whose description changes, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}

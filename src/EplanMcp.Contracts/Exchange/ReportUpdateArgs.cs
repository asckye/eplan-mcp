namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ReportUpdatePlan"/> and <see cref="OperationNames.ReportUpdateExecute"/>.</summary>
public sealed class ReportUpdateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>
    /// The full names of the report pages to update: 1 to 50, found as eplan_page_get finds a page. Null updates all
    /// reports of the project.
    /// </summary>
    public List<string>? Pages { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}

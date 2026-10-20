namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.CheckRunPlan"/> and <see cref="OperationNames.CheckRunExecute"/>.</summary>
public sealed class CheckRunArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>
    /// The full names of the pages to check: 1 to 50, found as eplan_page_get finds a page. Null checks the whole
    /// project.
    /// </summary>
    public List<string>? Pages { get; set; }

    /// <summary>The check scheme's name; null or blank for the scheme EPLAN uses now.</summary>
    public string? Scheme { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}

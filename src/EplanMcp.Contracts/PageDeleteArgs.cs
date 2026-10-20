namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PageDeletePlan"/> and <see cref="OperationNames.PageDeleteExecute"/>.</summary>
public sealed class PageDeleteArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full names of the pages to delete, matched exactly: 1 to 50.</summary>
    public List<string> Pages { get; set; } = [];

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}

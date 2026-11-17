namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ProjectTranslatePlan"/> and <see cref="OperationNames.ProjectTranslateExecute"/>.</summary>
public sealed class ProjectTranslateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>1 to 200 full page names whose texts, and those of the functions on them, are translated; null for the whole project.</summary>
    public List<string>? Pages { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}

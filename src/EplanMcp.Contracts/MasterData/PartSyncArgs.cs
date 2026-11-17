namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PartSyncPlan"/> and <see cref="OperationNames.PartSyncExecute"/>.</summary>
public sealed class PartSyncArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary><c>append_new</c> (the default), <c>overwrite_existing</c> or <c>overwrite_and_append</c>.</summary>
    public string? Mode { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}

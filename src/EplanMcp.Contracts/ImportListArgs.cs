namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ImportListPlan"/> and <see cref="OperationNames.ImportListExecute"/>.</summary>
public sealed class ImportListArgs
{
    /// <summary>Execution budget including the project backup, in seconds (tool spec 5).</summary>
    public const int ExecutionSeconds = 300;

    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary><c>devices</c> for device list entries; <c>parts</c> for the project's parts list.</summary>
    public string? Kind { get; set; }

    /// <summary>The relative path below the Host's imports folder.</summary>
    public string? File { get; set; }

    /// <summary>The full path resolved by the Host under SEC-31; never shown to the model.</summary>
    public string? FilePath { get; set; }

    /// <summary>The file's SHA-256 as the Host read it, 64 lowercase hex digits; checked again by the Bridge.</summary>
    public string? Sha256 { get; set; }

    /// <summary><c>xml</c> (default) or <c>csv</c>; parts accept only XML.</summary>
    public string? Format { get; set; }

    /// <summary>For execute: the plan's <see cref="WritePlan.Fingerprint"/>; null for plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For execute: the Host's new project snapshot target (ADR-0013); null for plan.</summary>
    public BackupTarget? Backup { get; set; }
}

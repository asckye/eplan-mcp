namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.ProjectCreatePlan"/>, <see cref="OperationNames.ProjectCreateExecute"/>,
/// <see cref="OperationNames.ProjectRestorePlan"/> and <see cref="OperationNames.ProjectRestoreExecute"/>
/// (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.8, 4.9): a basic project (.zw9) or a project backup (.zw1), and a new
/// empty folder for the new project.
/// </summary>
public sealed class NewProjectArgs
{
    /// <summary>The file's path below the import folder, with <c>/</c> between folders, as the Host shows it.</summary>
    public string? File { get; set; }

    /// <summary>The file's full path, which the Host resolved below its import root (SEC-31); never shown to the model (SEC-12).</summary>
    public string? FilePath { get; set; }

    /// <summary>The SHA-256 of the file as the Host read it, lowercase hex.</summary>
    public string? Sha256 { get; set; }

    /// <summary>The new project's name: its link file is <c>&lt;destination&gt;\&lt;name&gt;.elk</c>.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The full path of the folder the new project goes into, which the Host chose in its scratch folder: absent, or an
    /// empty folder. EPLAN overwrites a project when it restores onto one (api-notes A31), so nothing else is accepted.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>For the execute operations: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plans.</summary>
    public string? Fingerprint { get; set; }
}

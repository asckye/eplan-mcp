namespace EplanMcp.Contracts;

/// <summary>
/// Where a standalone backup or the execute operation of a Destructive write copies the project, and how long
/// that may take (ADR-0013, SEC-21). The Host makes the folder, new and empty, for this write only; the Bridge copies
/// the project there and refuses the write with BACKUP_FAILED when it cannot.
/// </summary>
public sealed class BackupTarget
{
    /// <summary>The copy's full path: <c>&lt;data root&gt;\backups\&lt;project&gt;\&lt;UTC time&gt;\&lt;project&gt;.elk</c>.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The copy/check budget in seconds: half a Destructive write's limit, or at most 60 for project.backup.</summary>
    public int MaxSeconds { get; set; }
}

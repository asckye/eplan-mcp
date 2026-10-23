namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ProjectBackup"/>; the Host supplies a new backup folder.</summary>
public sealed class ProjectBackupArgs
{
    /// <summary>The explicit name or path of a project that is already open.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The copy destination and budget; the standalone operation allows at most 60 seconds.</summary>
    public BackupTarget? Target { get; set; }
}

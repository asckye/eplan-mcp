namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.MasterDataBackup"/>: where EPLAN's <c>backup /TYPE:MASTERDATA</c> puts its
/// archive of the configured database before a master-data write, and how long that may take (ADR-0019). The Host makes
/// the folder, new and empty, for this write only; the Bridge refuses the write with BACKUP_FAILED when the backup fails.
/// </summary>
public sealed class MasterDataBackupArgs
{
    /// <summary>Which database: <see cref="MasterDataKinds.Parts"/> or <see cref="MasterDataKinds.Translations"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The configured database the write was planned for, as EPLAN's setting has it; a database configured otherwise
    /// meanwhile is PLAN_STALE, so the backup is always of the database the engineer approved.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The full path of the Host's new, empty folder: <c>&lt;data root&gt;\backups\masterdata\&lt;UTC time&gt;</c>.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>The archive's file name without extension; EPLAN adds <c>.zw6</c> for parts or <c>.zw7</c> for translations (S24).</summary>
    public string ArchiveName { get; set; } = string.Empty;

    /// <summary>The longest the backup and its check may take, in seconds.</summary>
    public int MaxSeconds { get; set; }
}

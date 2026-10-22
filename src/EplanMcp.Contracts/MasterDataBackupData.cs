namespace EplanMcp.Contracts;

/// <summary>What <see cref="OperationNames.MasterDataBackup"/> made: the archive of the configured database (ADR-0019).</summary>
public sealed class MasterDataBackupData
{
    /// <summary>The database backed up, as EPLAN's setting has it.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The archive's full path, the only file in the Host's folder.</summary>
    public string Archive { get; set; } = string.Empty;

    /// <summary>The archive's size in bytes, never 0.</summary>
    public long Bytes { get; set; }
}

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Backs up the master data EPLAN is configured with (ADR-0019, spike S24), implemented by the EPLAN adapter layer and
/// called by <see cref="MasterDataBackupOperation"/> only. Main thread only. Never selects, creates or upgrades a
/// database and never changes a setting.
/// </summary>
public interface IMasterDataBackup
{
    /// <summary>The configured parts database as EPLAN's setting has it: a file, with path variables, or a SQL Server connection string.</summary>
    public string PartsDatabase();

    /// <summary>
    /// Runs EPLAN's <c>backup /TYPE:MASTERDATA /MDTYPE:ARTICLES</c> of the file database <paramref name="database"/>
    /// into <paramref name="folder"/> as <paramref name="archiveName"/>; throws when EPLAN reports a failure. The caller
    /// checks what lies in the folder afterwards.
    /// </summary>
    public void BackupParts(string database, string folder, string archiveName);
}

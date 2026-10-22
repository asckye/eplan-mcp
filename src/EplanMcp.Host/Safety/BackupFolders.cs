using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Safety;

/// <summary>
/// The folders Destructive writes back the project up into (ADR-0013, SEC-21):
/// <c>&lt;data root&gt;\backups\&lt;project&gt;\&lt;UTC time&gt;\</c>, made new for every write and never reused, so a copy
/// never lands on another. Every level below the backup root must be no symbolic link or junction (SEC-19's rule), so the
/// copy lies where the folder's name says. A folder whose backup failed is renamed with <see cref="FailedSuffix"/>, so
/// nobody takes it for a usable backup. EplanMcp never deletes a backup.
/// </summary>
internal sealed class BackupFolders(HostDataLocations locations, TimeProvider clock)
{
    /// <summary>Added to the name of a folder whose backup failed.</summary>
    public const string FailedSuffix = "-failed";

    /// <summary>The message of BACKUP_FAILED (tool spec eplan_page_delete, section 4); it names no local path (SEC-12).</summary>
    public const string FailedMessage =
        "Nothing was changed because the project could not be backed up first. Ask the engineer to check the free disk space, or to run 'eplanmcp doctor'.";

    /// <summary>BACKUP_FAILED: the write did not run.</summary>
    public static BridgeError Failure() => new() { Reason = ErrorReason.BackupFailed, Message = FailedMessage, Execution = ExecutionState.NotStarted };

    /// <summary>The folder name below the backup root that holds the master-data backups (ADR-0019).</summary>
    public const string MasterData = "masterdata";

    /// <summary>The message of BACKUP_FAILED when the master data could not be backed up (ADR-0019); no local path (SEC-12).</summary>
    public const string MasterDataFailedMessage =
        "Nothing was changed because the parts database could not be backed up first. Ask the engineer to check the free disk space, or to run 'eplanmcp doctor'.";

    /// <summary>The folder that holds the backups of <paramref name="plan"/>'s project, as the approval dialog names it.</summary>
    public string ProjectFolder(WritePlan plan) => Path.Combine(locations.Backups, Path.GetFileName(plan.ProjectPath));

    /// <summary>The folder that holds the master-data backups, as the approval dialog names it: <c>&lt;data root&gt;\backups\masterdata</c>.</summary>
    public string MasterDataFolder => Path.Combine(locations.Backups, MasterData);

    /// <summary>BACKUP_FAILED for the master data: the write did not run.</summary>
    public static BridgeError MasterDataFailure() => new() { Reason = ErrorReason.BackupFailed, Message = MasterDataFailedMessage, Execution = ExecutionState.NotStarted };

    /// <summary>
    /// Makes a new, empty folder <c>&lt;data root&gt;\backups\masterdata\&lt;UTC time&gt;\</c> for one master-data backup
    /// (ADR-0019) and returns it, under the same rules as <see cref="Create"/>.
    /// </summary>
    public string CreateMasterData()
    {
        Directory.CreateDirectory(locations.Backups); // the root itself may be a link (SEC-19)
        return Level(Level(locations.Backups, MasterData, mayExist: true), Time(), mayExist: false);
    }

    /// <summary>
    /// Makes a new, empty folder for one backup of <paramref name="plan"/>'s project and returns the path of the copy in
    /// it, <c>&lt;project&gt;.elk</c>. Throws <see cref="IOException"/> when the folder exists already, leads through a
    /// link, or cannot be made, and <see cref="UnauthorizedAccessException"/> when that is not allowed.
    /// </summary>
    public string Create(WritePlan plan)
    {
        string project = Path.GetFileName(plan.ProjectPath);
        if (project.Length == 0 || project is "." or ".." || project.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new IOException("The project's file name cannot name a backup folder.");
        }

        Directory.CreateDirectory(locations.Backups); // the root itself may be a link (SEC-19)
        string folder = Level(Level(locations.Backups, project, mayExist: true), Time(), mayExist: false);
        return Path.Combine(folder, project + ".elk");
    }

    /// <summary>
    /// After a write that did not run: renames the folder of <paramref name="copy"/> with <see cref="FailedSuffix"/>
    /// when the backup failed or the folder holds anything; removes it when it is still empty, since it holds no backup
    /// then. Returns the folder's new path, or null when it was removed. Throws <see cref="IOException"/> on failure.
    /// </summary>
    public static string? Settle(string copy, bool failed) => SettleFolder(Path.GetDirectoryName(copy)!, failed);

    /// <summary>As <see cref="Settle"/>, for a backup folder itself, such as a master-data backup's.</summary>
    public static string? SettleFolder(string folder, bool failed)
    {
        if (!failed && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
            return null;
        }

        Directory.Move(folder, folder + FailedSuffix);
        return folder + FailedSuffix;
    }

    /// <summary>
    /// How many backups lie below <paramref name="root"/> (failed ones not counted) and how many bytes everything below
    /// it takes, without following links.
    /// </summary>
    public static (int Backups, long Bytes) Usage(string root)
    {
        var level = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        int backups = Directory.EnumerateDirectories(root, "*", level)
            .SelectMany(project => Directory.EnumerateDirectories(project, "*", level))
            .Count(folder => !folder.EndsWith(FailedSuffix, StringComparison.OrdinalIgnoreCase));
        var tree = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        long bytes = new DirectoryInfo(root).EnumerateFiles("*", tree).Sum(file => file.Length);
        return (backups, bytes);
    }

    private string Time() => clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The folder <paramref name="name"/> in <paramref name="parent"/>, made if needed; never a link.</summary>
    private static string Level(string parent, string name, bool mayExist)
    {
        string path = Path.Combine(parent, name);
        var folder = new DirectoryInfo(path);
        if (!mayExist && (folder.Exists || File.Exists(path)))
        {
            throw new IOException($"The backup folder '{path}' exists already, and a backup never reuses one.");
        }

        folder.Create();
        folder.Refresh();
        return (folder.Attributes & FileAttributes.ReparsePoint) != 0
            ? throw new IOException($"The backup folder '{path}' is a symbolic link or junction, which backups never follow.")
            : path;
    }
}

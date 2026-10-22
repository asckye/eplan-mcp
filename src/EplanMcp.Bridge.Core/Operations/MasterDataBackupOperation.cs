using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.MasterDataBackup"/>: the backup ADR-0019 makes after the engineer approved a master-data
/// write and before it runs. It backs up the database the write was planned for, never another (PLAN_STALE), never one
/// on SQL Server (POLICY_DENIED), with EPLAN's <c>backup /TYPE:MASTERDATA</c> into the Host's new, empty folder. The
/// folder must then hold exactly one file: a non-empty 7z archive with EPLAN's extension (S24 conclusion 5: 0.14 s and
/// one 991 KB <c>.zw6</c> for the sample parts database), within <see cref="MasterDataBackupArgs.MaxSeconds"/>;
/// otherwise BACKUP_FAILED. Every error is <c>not_started</c>: the database is not changed by a backup (S24).
/// </summary>
public sealed class MasterDataBackupOperation : Operation<MasterDataBackupArgs, MasterDataBackupData>
{
    /// <summary>What the model reads for BACKUP_FAILED; it names no local path (SEC-12).</summary>
    public const string FailedMessage =
        "Nothing was changed because the parts database could not be backed up first. Ask the engineer to check the free disk space, or to run 'eplanmcp doctor'.";

    /// <summary>The extension of a parts database archive (S24).</summary>
    public const string PartsExtension = ".zw6";

    // The first bytes of every 7z archive; S24 found EPLAN's master-data backups are 7z archives.
    private static readonly byte[] _sevenZip = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    private readonly IMasterDataBackup _backup;
    private readonly Func<Func<TimeSpan>> _startClock;

    public MasterDataBackupOperation(IMasterDataBackup backup)
        : this(backup, ProjectBackups.StartStopwatch)
    {
    }

    /// <summary>For tests: the stopwatch the time limit is measured with, which moves only when the fake backup takes time.</summary>
    internal MasterDataBackupOperation(IMasterDataBackup backup, Func<Func<TimeSpan>> startClock)
    {
        _backup = backup;
        _startClock = startClock;
    }

    public override string Name => OperationNames.MasterDataBackup;

    protected override MasterDataBackupData Execute(MasterDataBackupArgs args)
    {
        if (args.Kind != MasterDataKinds.Parts)
        {
            throw WriteArguments.NotStarted(InvalidArgument($"'kind' must be '{MasterDataKinds.Parts}'."));
        }

        string folder = Checked(args);
        string configured = _backup.PartsDatabase() ?? string.Empty;
        if (MasterDatabases.IsServer(configured))
        {
            throw MasterDatabases.Denied();
        }

        if (configured.Length == 0 || !string.Equals(configured, args.Database, StringComparison.Ordinal))
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(
                ErrorReason.PlanStale,
                "EPLAN is configured with another parts database than the one the change was planned and approved for, so nothing was backed up or changed. Call the tool again for a new plan."));
        }

        Func<TimeSpan> elapsed = _startClock();
        try
        {
            _backup.BackupParts(configured, folder, args.ArchiveName);
        }
        catch (BridgeErrorException known)
        {
            throw WriteArguments.NotStarted(known);
        }
        catch (Exception ex)
        {
            throw Failed("EPLAN could not back the parts database up.", ex);
        }

        string archive = Archive(folder);
        TimeSpan took = elapsed();
        if (took.TotalSeconds > args.MaxSeconds)
        {
            throw Failed($"The backup took {Number((long)took.TotalSeconds)} s, more than the {Number(args.MaxSeconds)} s it may take.");
        }

        return new MasterDataBackupData { Database = configured, Archive = archive, Bytes = new FileInfo(archive).Length };
    }

    /// <summary>
    /// The folder when the arguments are what the Host makes: a fully qualified local path to a folder that exists and
    /// is empty, a plain archive name and a positive time limit (a backup never overwrites anything, as SEC-21).
    /// </summary>
    private static string Checked(MasterDataBackupArgs args)
    {
        string folder = args.Folder ?? string.Empty;
        string name = args.ArchiveName ?? string.Empty;
        bool local = folder.Length > 3 && char.IsLetter(folder[0]) && folder[1] == ':' && folder[2] == '\\' && folder.IndexOf("..", StringComparison.Ordinal) < 0;
        return args.MaxSeconds > 0 && local && IsPlainName(name) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()
            ? folder
            : throw Failed("The write came without a new, empty backup folder.");
    }

    /// <summary>An archive name as S24 used it: 1 to 64 ASCII letters, digits, <c>-</c> and <c>_</c>.</summary>
    public static bool IsPlainName(string name) =>
        name.Length is > 0 and <= 64 && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_') && name.All(c => c < 128);

    /// <summary>
    /// What is wrong with <paramref name="folder"/> after a backup, or null when its only entry is a non-empty 7z archive
    /// with the parts extension; eplan_part_export's backup format checks its folder the same way.
    /// </summary>
    public static string? ArchiveProblem(string folder)
    {
        string[] entries = Directory.GetFileSystemEntries(folder);
        if (entries.Length != 1 || !File.Exists(entries[0]) || !entries[0].EndsWith(PartsExtension, StringComparison.OrdinalIgnoreCase))
        {
            return $"The backup folder holds {Number(entries.Length)} entries instead of one {PartsExtension} archive.";
        }

        byte[] head = new byte[_sevenZip.Length];
        int read;
        using (var stream = new FileStream(entries[0], FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            read = stream.Read(head, 0, head.Length);
        }

        return read == head.Length && head.SequenceEqual(_sevenZip) ? null : "The backup archive is empty or not an archive.";
    }

    /// <summary>The only entry of <paramref name="folder"/>, which must be a non-empty 7z archive with the parts extension.</summary>
    private static string Archive(string folder) =>
        ArchiveProblem(folder) is { } problem ? throw Failed(problem) : Directory.GetFileSystemEntries(folder)[0];

    /// <summary>BACKUP_FAILED with the problem in <c>metadata.problem</c>; the router logs the cause, or the problem itself.</summary>
    private static BridgeErrorException Failed(string problem, Exception? cause = null)
    {
        var error = new BridgeErrorException(ErrorReason.BackupFailed, FailedMessage, cause ?? new InvalidOperationException(problem));
        error.Error.Metadata = new Dictionary<string, string> { ["problem"] = problem };
        return WriteArguments.NotStarted(error);
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}

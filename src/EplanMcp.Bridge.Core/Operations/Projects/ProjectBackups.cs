using System.Diagnostics;
using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The backup step of every Destructive execute operation (ADR-0013, SEC-21, tool spec eplan_page_delete section 6):
/// after the fingerprint check and before the transaction, in the same main-thread call, so the engineer cannot change
/// the project in between (M4 design 4.3). The copy goes into the Host's new, empty folder, never over a file, and must
/// have the project's page count within <see cref="BackupTarget.MaxSeconds"/>; otherwise the write does not run
/// (BACKUP_FAILED, not_started). Main thread only.
/// </summary>
public static class ProjectBackups
{
    /// <summary>What the model reads for BACKUP_FAILED; the same as the Host's, and without a local path (SEC-12).</summary>
    public const string FailedMessage =
        "Nothing was changed because the project could not be backed up first. Ask the engineer to check the free disk space, or to run 'eplanmcp doctor'.";

    /// <summary>Copies <paramref name="project"/> to <paramref name="target"/> and checks the copy; returns the copy's path.</summary>
    public static string Make(IProjectBackup backup, IPageReader pages, OpenProject project, BackupTarget? target) =>
        Make(backup, pages, project, target, StartStopwatch);

    /// <summary>
    /// A stopwatch that has just started, as what it reads: the backup is timed with it. Tests give one that moves only
    /// when the fake EPLAN takes time, so that a slow machine cannot make a backup slower than it was.
    /// </summary>
    internal static Func<TimeSpan> StartStopwatch()
    {
        var watch = Stopwatch.StartNew();
        return () => watch.Elapsed;
    }

    internal static string Make(IProjectBackup backup, IPageReader pages, OpenProject project, BackupTarget? target, Func<Func<TimeSpan>> startClock)
        => MakeData(backup, pages, project, target, startClock).Path;

    /// <summary>Shares the checked copy and its measurements with the standalone backup operation.</summary>
    internal static ProjectBackupData MakeData(IProjectBackup backup, IPageReader pages, OpenProject project, BackupTarget? target, Func<Func<TimeSpan>> startClock)
    {
        string copy = Checked(target);
        Func<TimeSpan> elapsed = startClock();
        int expected = pages.PageNames(project.Path).Count;
        int copied;
        try
        {
            backup.Copy(project.Path, copy);
            copied = backup.CountPages(copy);
        }
        catch (BridgeErrorException known)
        {
            throw WriteArguments.NotStarted(known);
        }
        catch (Exception ex)
        {
            throw Failed("EPLAN could not copy the project or read the copy.", ex);
        }

        if (copied != expected)
        {
            throw Failed($"The copy has {Number(copied)} pages, the project {Number(expected)}.");
        }

        TimeSpan took = elapsed();
        if (took.TotalSeconds > target!.MaxSeconds)
        {
            throw Failed($"The backup took {Number((int)took.TotalSeconds)} s, more than the {Number(target.MaxSeconds)} s it may take.");
        }

        // A safe point: the copy is made and EPLAN has returned; a cancel now still leaves the project unchanged.
        OperationContext.Current.ThrowIfCancellationRequested();
        return new ProjectBackupData { Path = copy, Pages = copied, Seconds = (decimal)took.TotalSeconds };
    }

    /// <summary>
    /// The copy's path when <paramref name="target"/> is what the Host makes: a fully qualified local <c>.elk</c> path
    /// in a folder that exists and is empty, and a positive time limit (SEC-21: a backup never overwrites anything).
    /// </summary>
    private static string Checked(BackupTarget? target)
    {
        string path = target?.Path ?? string.Empty;
        bool local = path.Length > 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\' && path.IndexOf("..", StringComparison.Ordinal) < 0;
        string? folder = local ? Path.GetDirectoryName(path) : null;
        return target is { MaxSeconds: > 0 }
            && local
            && path.EndsWith(".elk", StringComparison.OrdinalIgnoreCase)
            && folder is not null
            && Directory.Exists(folder)
            && !Directory.EnumerateFileSystemEntries(folder).Any()
            ? path
            : throw Failed("The write came without a new, empty backup folder.");
    }

    /// <summary>BACKUP_FAILED with the problem in <c>metadata.problem</c>; the router logs the cause, or the problem itself.</summary>
    internal static BridgeErrorException Failed(string problem, Exception? cause = null)
    {
        var error = new BridgeErrorException(ErrorReason.BackupFailed, FailedMessage, cause ?? new InvalidOperationException(problem));
        error.Error.Metadata = new Dictionary<string, string> { ["problem"] = problem };
        return WriteArguments.NotStarted(error);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

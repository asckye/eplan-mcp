using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Initial plan backup (design 4.4): runs in the dispatcher's LockingStep, without a transaction or undo step.
/// Reuses the Destructive backup path: Snapshot plus read-only page count (A18/A41).
/// </summary>
public sealed class ProjectBackupOperation : Operation<ProjectBackupArgs, ProjectBackupData>
{
    public const int MaxSeconds = 60;
    private readonly IProjectCatalog _catalog;
    private readonly IPageReader _pages;
    private readonly IProjectBackup _backup;
    private readonly Func<Func<TimeSpan>> _startClock;

    public ProjectBackupOperation(IProjectCatalog catalog, IPageReader pages, IProjectBackup backup)
        : this(catalog, pages, backup, ProjectBackups.StartStopwatch)
    {
    }

    internal ProjectBackupOperation(IProjectCatalog catalog, IPageReader pages, IProjectBackup backup, Func<Func<TimeSpan>> startClock)
    {
        _catalog = catalog;
        _pages = pages;
        _backup = backup;
        _startClock = startClock;
    }

    public override string Name => OperationNames.ProjectBackup;

    protected override ProjectBackupData Execute(ProjectBackupArgs args)
    {
        try
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(args.Project))
            {
                throw InvalidArgument("'project' must name an open project.");
            }

            OpenProject project = ProjectResolver.Resolve(_catalog.ListOpenProjects(), args.Project);
            BackupTarget? target = args.Target is { } given
                ? new BackupTarget { Path = given.Path, MaxSeconds = Math.Min(given.MaxSeconds, MaxSeconds) }
                : null;
            return ProjectBackups.MakeData(_backup, _pages, project, target, _startClock);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ProjectBackups.Failed("EPLAN could not copy the project or read the copy.", ex);
        }
    }
}

using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The backup before a Destructive write (ADR-0013). Main thread, inside the dispatcher's LockingStep and, as in spike
/// S12, under QuietModeStep(ShowNoDialogs) (coding standards 9.4, rule 11): no EPLAN dialog may block the dispatcher.
/// Neither call changes the project, so neither needs a TransactionScope.
/// </summary>
internal sealed class EplanProjectBackup : IProjectBackup
{
    /// <summary>
    /// <c>CopyProject(link file, copy, CopyMode.Snapshot)</c>: S12 copied the open sample project this way in 0.6–0.7 s,
    /// left it open and unlocked, and the copy held a write committed just before (api-notes A18). <c>CopyMode.All</c>
    /// rewrites the project's lock files and <c>Backup.Project</c> its data files, so neither is used.
    /// </summary>
    public void Copy(string projectPath, string copyPath)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        using var manager = new ProjectManager();
        manager.CopyProject(project.ProjectLinkFilePath, copyPath, ProjectManager.CopyMode.Snapshot);
    }

    /// <summary>
    /// Opens the copy read-only, counts its pages as <see cref="EplanPageReader.PageNames"/> counts the project's, and
    /// closes it again. S12 counted the copy's pages after opening it through the Remote Client; opening it in the
    /// dispatcher (its duration, and whether EPLAN's current project changes meanwhile) is not verified yet.
    /// </summary>
    public int CountPages(string copyPath)
    {
        MainThread.Assert();
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        using var manager = new ProjectManager();
        bool lockByDefault = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false; // as EplanProjects.Read: counting must not take locks
        try
        {
            Project copy = manager.OpenProject(copyPath, ProjectManager.OpenMode.ReadOnly);
            try
            {
                using var finder = new DMObjectsFinder(copy);
                return finder.GetPages(null).Length;
            }
            finally
            {
                copy.Close();
            }
        }
        finally
        {
            manager.LockProjectByDefault = lockByDefault;
        }
    }
}

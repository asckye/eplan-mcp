using System.Security;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Opens, closes, copies and removes projects with no dialog (api-notes A107): opened in standard mode as the engineer opens
/// a project (owner 2026-11-09: no "exclusive" mark; writes work in standard mode, A128). EPLAN selects the
/// project only when no other is open. The project object is not kept; a later call looks it up again.
/// </summary>
internal sealed class EplanProjectSessions : IProjectSessions
{
    public void Open(string linkFile)
    {
        MainThread.Assert();
        Guard(() =>
        {
            using var manager = new ProjectManager();
            manager.OpenProject(linkFile, ProjectManager.OpenMode.Standard);
        });
    }

    public void Close(string projectPath)
    {
        MainThread.Assert();
        Guard(() => EplanProjects.ByPath(projectPath).Close());
    }

    /// <summary>Snapshot copy as the backup makes it (A18): the project may stay open and keeps no lock.</summary>
    public void Copy(string linkFile, string newLinkFile)
    {
        MainThread.Assert();
        Guard(() =>
        {
            using var manager = new ProjectManager();
            manager.CopyProject(linkFile, newLinkFile, ProjectManager.CopyMode.Snapshot);
        });
    }

    /// <summary>The official page does not say which files RemoveProject deletes; the caller reads back what is left.</summary>
    public void Remove(string linkFile)
    {
        MainThread.Assert();
        Guard(() =>
        {
            using var manager = new ProjectManager();
            manager.RemoveProject(linkFile);
        });
    }

    private static void Guard(System.Action change)
    {
        try
        {
            using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
            change();
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException or NotSupportedException or SecurityException)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused: " + ex.Message, ex);
        }
    }
}

using System.Security;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Opens and closes projects with no dialog (api-notes A107): opened exclusively, as the writes need. EPLAN selects the
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
            manager.OpenProject(linkFile, ProjectManager.OpenMode.Exclusive);
        });
    }

    public void Close(string projectPath)
    {
        MainThread.Assert();
        Guard(() => EplanProjects.ByPath(projectPath).Close());
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

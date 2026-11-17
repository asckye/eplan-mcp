using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The project's stored master data through <c>Masterdata</c> (api-notes A111). A new project keeps the forms its template
/// stored; EPLAN offers an update when the project is opened with dialogs, and <see cref="Update"/> is that update without one.
/// </summary>
internal sealed class EplanProjectMasterData : IProjectMasterData
{
    public IReadOnlyList<string> Entries(string projectPath)
    {
        MainThread.Assert();
        using var masterdata = new Masterdata();
        var entries = new List<string>();
        foreach (string? entry in masterdata.get_ProjectEntries(EplanProjects.ByPath(projectPath)))
        {
            if (entry is not null)
            { entries.Add(entry); }
        }
        return entries;
    }

    public void Update(string projectPath)
    {
        MainThread.Assert();
        using var masterdata = new Masterdata();
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        try
        {
            masterdata.UpdateProject(EplanProjects.ByPath(projectPath));
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.OutcomeUnknown, "EPLAN stopped while updating the project's master data, so some files may be updated. Do not repeat; the backup holds the project as it was: " + ex.Message, ex)
            { Error = { Execution = ExecutionState.Unknown } };
        }
    }
}

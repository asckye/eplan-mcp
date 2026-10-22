using System.Security;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Makes new projects the way spikes S27 and S30 did in the dispatcher (api-notes A31, A34), under
/// QuietModeStep(ShowNoDialogs) so that no dialog can stop the main thread (coding standards 11). Main thread, inside the
/// dispatcher's LockingStep; there is no transaction: the operation deletes what a failed attempt made.
/// </summary>
internal sealed class EplanProjectFactory : IProjectFactory
{
    /// <summary>
    /// S27 made a project from GB_bas001.zw9 in 3.5 s (S30: 3 to 9 s), open exclusively for the API. A34: the object
    /// CreateProject returns cannot be closed once its ProjectManager is gone and OpenProject on it throws, and the project
    /// is no longer open after the call; so what is needed is read here, and the project is neither closed nor reopened.
    /// </summary>
    public (int Pages, string? ProjectGuid) Create(string linkFile, string basicProject)
    {
        MainThread.Assert();
        try
        {
            using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
            using var manager = new ProjectManager();
            Project project = manager.CreateProject(linkFile, basicProject);
            using var finder = new DMObjectsFinder(project);
            int pages = finder.GetPages(null).Length;
            using ProjectPropertyList properties = project.Properties;
            using PropertyValue guid = properties.PROJ_GUID;
            return (pages, guid.IsEmpty ? null : guid.ToString());
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException or NotSupportedException or SecurityException or IOException
            && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not create a project from the basic project. metadata.eplanMessage has EPLAN's message; the file may be damaged, or from a newer EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}

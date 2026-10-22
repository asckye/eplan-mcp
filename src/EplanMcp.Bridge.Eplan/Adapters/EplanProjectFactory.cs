using System.Security;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Makes new projects the way spikes S27 and S30 did (api-notes A31, A34), under
/// QuietModeStep(ShowNoDialogs) so that no dialog can stop the main thread (coding standards 11). Main thread, inside the
/// dispatcher's LockingStep; there is no transaction: the operation deletes what a failed attempt made.
/// </summary>
internal sealed class EplanProjectFactory : IProjectFactory
{
    /// <summary>
    /// S27 made a project from GB_bas001.zw9 in 3.5 s (S30: 3 to 9 s), open exclusively for the API.
    /// S30 saw the project closed after a spike call, but it stays open through the Bridge (2026-10-22 Large test ProjectCreateTests).
    /// Read what is needed before disposing the creating ProjectManager, then close through a fresh OpenProjects lookup
    /// (S30 r3, api-notes A34): the object CreateProject returns cannot be closed once its ProjectManager is gone.
    /// </summary>
    public (int Pages, string? ProjectGuid) Create(string linkFile, string basicProject)
    {
        MainThread.Assert();
        try
        {
            using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
            (int Pages, string? ProjectGuid) result;
            using (var manager = new ProjectManager())
            {
                Project project = manager.CreateProject(linkFile, basicProject);
                using var finder = new DMObjectsFinder(project);
                int pages = finder.GetPages(null).Length;
                using ProjectPropertyList properties = project.Properties;
                using PropertyValue guid = properties.PROJ_GUID;
                result = (pages, guid.IsEmpty ? null : guid.ToString());
            }

            using var closeManager = new ProjectManager();
            Project? openProject = closeManager.OpenProjects.FirstOrDefault(p => string.Equals(p.ProjectLinkFilePath, linkFile, StringComparison.OrdinalIgnoreCase));
            openProject?.Close();
            return result;
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

    /// <summary>
    /// The <c>restore</c> action with <c>TYPE=PROJECT</c>, <c>ARCHIVENAME</c> and <c>PROJECTNAME</c>, the parameters S27
    /// gave it through the Remote Client (api-notes A31: about 6 s for the sample project; it overwrites an existing project
    /// without a word, so the operation has checked that the folder is empty). Each parameter is added on its own, never a
    /// command line, as the master-data backup does (A30: command lines garbled Chinese paths). Running it in the
    /// dispatcher is not verified yet (Large test ProjectRestoreTests). The project is not opened.
    /// </summary>
    public void Restore(string archive, string linkFile)
    {
        MainThread.Assert();
        using var context = new ActionCallingContext();
        context.AddParameter("TYPE", "PROJECT");
        context.AddParameter("ARCHIVENAME", archive);
        context.AddParameter("PROJECTNAME", linkFile);
        bool done;
        using (new QuietModeStep(QuietModes.ShowNoDialogs))
        {
#pragma warning disable RS0030 // Restores a project backup into the empty folder the operation checked, and writes only there (A31).
            var action = new ActionManager().FindAction("restore") ?? throw new InvalidOperationException("EPLAN has no 'restore' action.");
#pragma warning restore RS0030
            done = action.Execute(context);
        }

        if (!done)
        {
            throw new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN's restore action reported a failure: the backup may be damaged, or from a newer EPLAN. EPLAN's system messages may say why.");
        }
    }
}

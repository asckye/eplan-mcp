using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Exports a whole project the way spike S25 did (api-notes A25): in QuietModeStep(ShowNoDialogs) without SafetyPoint
/// or UndoStep, because these exports change no design data (S25: only User.eod), always to a file in the Host's
/// folder. Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanProjectExporter : IProjectExporter
{
    /// <summary>
    /// <c>Export.PXFProject</c> with connections, as S25 ran it; the overload with two arguments leaves the connections
    /// out (EPLAN XML docs). S25: 19 s and 598 MB for the sample project without master data, 37 s and 840 MB with it.
    /// </summary>
    public void ExportPxf(string projectPath, string file, bool includeMasterData) => Run("the project as PXF", () =>
    {
        using var export = new Export();
        export.PXFProject(EplanProjects.ByPath(projectPath), file, includeMasterData, true);
    });

    /// <summary>
    /// Runs one export quietly. What EPLAN throws for a failed export (EPLAN XML docs) goes back as
    /// EPLAN_OPERATION_FAILED with EPLAN's message; what the exception mapper knows (a missing licence) stays for it.
    /// </summary>
    private static void Run(string what, System.Action export)
    {
        MainThread.Assert();
        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                export();
            }
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or UnauthorizedAccessException or ApplicationException or IOException
            && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN could not export {what}, so no files were kept. metadata.eplanMessage has EPLAN's message; ask the engineer to check the project in EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}

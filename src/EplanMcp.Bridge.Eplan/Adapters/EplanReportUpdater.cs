using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads report pages and updates reports the way spike S11 did in the dispatcher (api-notes A16):
/// <c>Reports.GenerateProject(project)</c> for all reports, <c>Reports.Update(page)</c> for one report page. A report page
/// is a page with a report block (EPLAN XML docs: <c>Page.ReportBlock</c> is null when no report is assigned). Main
/// thread, inside the dispatcher's LockingStep; the updates inside the write's TransactionScope, whose SafetyPoint takes
/// them back (A16). Only the objects this class creates are disposed.
/// </summary>
internal sealed class EplanReportUpdater : IReportUpdater
{
    public IReadOnlyList<ReportPageRecord> ReportPages(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));

        // A null filter returns every page, in EPLAN's page order (S6).
        return
        [
            .. finder.GetPages(null)
                .Where(page => page.ReportBlock is not null)
                .Select(page => new ReportPageRecord { Name = page.Name, Id = page.ToStringIdentifier() }),
        ];
    }

    public void UpdateAll(string projectPath)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        Run(() =>
        {
            using var reports = new Reports();
            reports.GenerateProject(project);
        });
    }

    public bool Update(string projectPath, string page)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        if (EplanDeviceReader.FindPage(finder, page) is not { } target)
        {
            return false;
        }

        Run(() =>
        {
            using var reports = new Reports();
            reports.Update(target);
        });
        return true;
    }

    /// <summary>
    /// EPLAN reports a failed update as a BaseException, or, as the XML docs say for the other report methods, an
    /// ApplicationException with its message; either goes back as EPLAN_OPERATION_FAILED. What the exception mapper
    /// knows (locks, a missing licence) stays for it.
    /// </summary>
    private static void Run(Action update)
    {
        try
        {
            update();
        }
        catch (Exception ex) when ((ex is BaseException or ApplicationException) && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not update the reports, so nothing was changed. metadata.eplanMessage has EPLAN's message; ask the engineer to update the reports in EPLAN to see why.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}

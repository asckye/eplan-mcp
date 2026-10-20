using System.Collections;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.HEServices.Exceptions;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Runs EPLAN's check the way spike S11 did in the dispatcher (api-notes A16): <c>Check.VerifyProject(project, scheme,
/// false)</c> for the whole project, never re-checking only the messages marked as done, and
/// <c>Check.VerifyPages(pages, scheme)</c> for given pages. Main thread, inside the dispatcher's LockingStep;
/// <see cref="Run"/> inside the write's TransactionScope, whose SafetyPoint takes the new messages back (A16). Only the
/// objects this class creates are disposed.
/// </summary>
internal sealed class EplanCheckRunner : ICheckRunner
{
    public IReadOnlyList<string> PageIds(string projectPath, IReadOnlyList<string>? pages)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));

        // A null filter returns every page, in EPLAN's page order (S6).
        Page[] scope = pages is null ? finder.GetPages(null) : Find(finder, pages);
        return [.. scope.Select(page => page.ToStringIdentifier())];
    }

    /// <summary>
    /// An empty scheme is the one EPLAN uses now (EPLAN XML docs). A scheme EPLAN does not know is InvalidScheme for
    /// VerifyPages (EPLAN XML docs; A18 found its message lists the known schemes for renumbering); what VerifyProject
    /// does with one is not verified. EPLAN's other failures are BaseExceptions with its message; what the exception
    /// mapper knows (locks, a missing licence) stays for it.
    /// </summary>
    public void Run(string projectPath, IReadOnlyList<string>? pages, string scheme)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        try
        {
#pragma warning disable RS0030 // Running the check is this write's change, inside its TransactionScope (eplan_check_run, tool spec 5).
            using var check = new Check();
            if (pages is null)
            {
                check.VerifyProject(project, scheme, false);
            }
            else
            {
                using var finder = new DMObjectsFinder(project);
                check.VerifyPages(new ArrayList(Find(finder, pages)), scheme);
            }
#pragma warning restore RS0030
        }
        catch (InvalidScheme ex)
        {
            const string Violation = "'scheme' is not a check scheme EPLAN knows; omit it to check with the scheme EPLAN uses now.";
            var invalid = new BridgeErrorException(ErrorReason.InvalidArgument, "Invalid arguments: " + Violation + " Nothing was checked.", ex);
            invalid.Error.Metadata = new Dictionary<string, string>
            {
                ["violations"] = Violation,
                ["scheme"] = scheme,
                ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message),
            };
            throw invalid;
        }
        catch (BaseException ex) when (new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not run the check, so nothing was changed. metadata.eplanMessage has EPLAN's message; ask the engineer to run the check in EPLAN to see why.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }

    /// <summary>The pages by their exact names, which the operation resolved in the same main-thread call.</summary>
    private static Page[] Find(DMObjectsFinder finder, IReadOnlyList<string> pages) =>
        [.. pages.Select(name => EplanDeviceReader.FindPage(finder, name) ?? throw new InvalidOperationException("A page the operation resolved is gone."))];
}

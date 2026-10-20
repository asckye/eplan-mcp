using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads and removes pages the way spike S12 removed one (api-notes A18): the page by its exact name, then
/// <c>Page.Remove()</c>, which leaves functions of the same tag on other pages in place. Main thread, inside the
/// dispatcher's LockingStep; <see cref="Remove"/> inside the write's TransactionScope, whose SafetyPoint S12 saw roll a
/// removal back.
/// </summary>
internal sealed class EplanPageRemover : IPageRemover
{
    /// <summary>The page and <c>Page.AllPlacements</c>, every placement on it, by their session identifiers (A3).</summary>
    public RemovablePage? ReadPage(string projectPath, string pageName)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return EplanDeviceReader.FindPage(finder, pageName) is { } page
            ? new RemovablePage
            {
                Name = page.Name,
                Type = page.PageType.ToString(),
                ObjectIds = [page.ToStringIdentifier(), .. page.AllPlacements.Select(placement => placement.ToStringIdentifier())],
            }
            : null;
    }

    /// <summary>All functions without a filter, which returns each tag's main function once (A12).</summary>
    public IReadOnlyList<PlacedFunction> Functions(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return
        [
            .. finder.GetFunctions(null).Select(function => new PlacedFunction
            {
                Name = function.Name ?? string.Empty,
                IsMain = function.IsMainFunction,
                Page = function.Page?.Name,
                Id = function.ToStringIdentifier(),
            }),
        ];
    }

    /// <summary>
    /// EPLAN reports a page it cannot remove with a BaseException or a RemoveFailedException (EPLAN XML docs), which goes
    /// back as EPLAN_OPERATION_FAILED with EPLAN's message; what the exception mapper knows (locks, a missing licence)
    /// stays for it.
    /// </summary>
    public void Remove(string projectPath, string pageName)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Page page = EplanDeviceReader.FindPage(finder, pageName) ?? throw new InvalidOperationException("A page the operation resolved is gone.");
        try
        {
            page.Remove();
        }
        catch (Exception ex) when (ex is BaseException or RemoveFailedException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not delete a page, so nothing was deleted. metadata.eplanMessage has EPLAN's message; ask the engineer to check the page in EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["page"] = pageName, ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}

using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanRenumber = Eplan.EplApi.HEServices.Renumber;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Renumbers pages the way spike S12 did (api-notes A18): the pages by their exact names, then
/// <c>Renumber.Pages(pages, true, start, step, false, keepText, subpages)</c>, per structure and without keeping the
/// intervals; the new names are read from the same Page objects afterwards. Main thread, inside the dispatcher's
/// LockingStep; <see cref="Renumber"/> inside the write's TransactionScope, whose SafetyPoint S12 saw roll it back.
/// </summary>
internal sealed class EplanPageRenumberer : IPageRenumberer
{
    public string? PageId(string projectPath, string pageName)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return EplanDeviceReader.FindPage(finder, pageName)?.ToStringIdentifier();
    }

    /// <summary>
    /// <c>subpages</c> maps to <c>Renumber.Enums.SubPages</c>: Retain, ConsecutiveNumbering, ConvertIntoMainPages (XML;
    /// only Retain verified in S12). A name conflict EPLAN reports becomes ALREADY_EXISTS; other EPLAN failures
    /// EPLAN_OPERATION_FAILED with EPLAN's message.
    /// </summary>
    public IReadOnlyList<string> Renumber(string projectPath, IReadOnlyList<string> pages, int start, int increment, bool keepText, string subpages)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Page[] targets = [.. pages.Select(name => EplanDeviceReader.FindPage(finder, name) ?? throw new InvalidOperationException("A page the operation resolved is gone."))];
        EplanRenumber.Enums.SubPages mode = subpages switch
        {
            "consecutive" => EplanRenumber.Enums.SubPages.ConsecutiveNumbering,
            "to_main" => EplanRenumber.Enums.SubPages.ConvertIntoMainPages,
            _ => EplanRenumber.Enums.SubPages.Retain,
        };
        using var renumber = new EplanRenumber();
        try
        {
            renumber.Pages(targets, true, start, increment, false, keepText, mode);
        }
        catch (NameConflictException ex)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                "EPLAN reports that a new page number is taken, so nothing was renumbered. Choose another 'start'; eplan_page_list with name_prefix shows the numbers in use.",
                ex);
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not renumber the pages, so nothing was renumbered. metadata.eplanMessage has EPLAN's message; ask the engineer to check the pages in EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }

        return [.. targets.Select(page => page.Name)];
    }
}

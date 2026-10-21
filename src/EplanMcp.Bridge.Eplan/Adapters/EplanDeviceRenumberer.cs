using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices.Exceptions;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanRenumber = Eplan.EplApi.HEServices.Renumber;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Numbers device tags the way spike S12 did (api-notes A18): the functions in scope, read with a page filter as
/// <c>eplan_page_get</c> reads a page's, given to <c>Renumber.DeviceTags(IFunctionBase[], null, flags, scheme, start,
/// step)</c>, the overload whose fourth parameter is the scheme (EPLAN's XML lists the <c>Project</c> overload's
/// parameter names in another order, so it is not followed). Main thread, inside the dispatcher's LockingStep;
/// <see cref="Renumber"/> inside the write's TransactionScope, whose SafetyPoint S12 saw roll a numbering back.
/// </summary>
internal sealed class EplanDeviceRenumberer : IDeviceRenumberer
{
    public IReadOnlyList<PlacedFunction>? ReadFunctions(string projectPath, string? page)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return Functions(finder, page) is { } functions
            ? [.. functions.Select(f => new PlacedFunction { Name = f.Name ?? string.Empty, IsMain = f.IsMainFunction, Page = f.Page?.Name, Id = f.ToStringIdentifier() })]
            : null;
    }

    /// <summary>
    /// No filter scheme (null, XML); PostNumerate numbers only tags with '?' (XML, S12), no flag every tag in scope
    /// (not verified). The scheme "" was the same as "Standard" in S12. EPLAN's InvalidScheme names the schemes it has;
    /// other EPLAN failures, such as finding nothing to number, go back as EPLAN_OPERATION_FAILED with EPLAN's message.
    /// </summary>
    public void Renumber(string projectPath, IReadOnlyList<string>? pages, bool onlyUnnumbered, string? scheme, int start, int increment)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Function[] inScope = pages is null
            ? finder.GetFunctions(null)
            : [.. pages.SelectMany(page => Functions(finder, page) ?? throw new InvalidOperationException("A page the operation resolved is gone."))];

        // PostNumerate raised an access violation, which ends EPLAN, when it got a function whose device tag has no '?':
        // a terminal numbered only as ':?1', or a cable already numbered (live check, api-notes A20). It numbers only the
        // tags with '?' anyway, so only those go to it.
        IFunctionBase[] functions = onlyUnnumbered ? [.. inScope.Where(f => Unnumbered(f.Name))] : [.. inScope];
        if (functions.Length == 0)
        {
            return;
        }

        using var renumber = new EplanRenumber();
        try
        {
            renumber.DeviceTags(functions, null, onlyUnnumbered ? EplanRenumber.Enums.DeviceTags.PostNumerate : default, scheme ?? string.Empty, start, increment);
        }
        catch (InvalidScheme ex)
        {
            throw NumberingSchemes.Unknown(scheme, ex.Message, ex);
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not number the device tags, so nothing was numbered. metadata.eplanMessage has EPLAN's message; tags with '?' that EPLAN cannot number are for the engineer to number in EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }

    /// <summary>All functions, or those on the page with exactly this name (FunctionsFilter.Page, S6); null without such a page.</summary>
    /// <summary>A '?' in the device tag before any connection point: the rule of DeviceRenumberRequest.Unnumbered.</summary>
    private static bool Unnumbered(string? tag) => tag is not null && tag.Split(':')[0].IndexOf('?') >= 0;

    private static Function[]? Functions(DMObjectsFinder finder, string? page)
    {
        if (page is null)
        {
            return finder.GetFunctions(null);
        }

        if (EplanDeviceReader.FindPage(finder, page) is not { } target)
        {
            return null;
        }

        using var filter = new FunctionsFilter { Page = target };
        return finder.GetFunctions(filter);
    }
}

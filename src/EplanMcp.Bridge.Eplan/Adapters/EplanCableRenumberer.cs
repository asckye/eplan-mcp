using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.HEServices.Exceptions;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Numbers cables the way spike S21 did (api-notes A24): the functions of category Cable, read by category rather than
/// through <c>GetCablesWithFilterScheme</c>, whose empty filter depends on the cable navigator, given to
/// <c>CableService.RenumberCables</c>; the schemes from the project setting S21 found. Main thread, inside the dispatcher's
/// LockingStep; <see cref="Renumber"/> inside the write's TransactionScope, whose SafetyPoint S21 saw roll it back.
/// </summary>
internal sealed class EplanCableRenumberer : ICableRenumberer
{
    private const string SchemeNode = "CableLog.CableNumberingScheme";

    public IReadOnlyList<string> Schemes(string projectPath) => EplanSchemes.OfProject(SchemeNode, projectPath);

    public IReadOnlyList<PlacedFunction> ReadCables(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. Cables(finder).Select(f => new PlacedFunction { Name = f.Name ?? string.Empty, IsMain = f.IsMainFunction, Page = f.Page?.Name, Id = f.ToStringIdentifier() })];
    }

    /// <summary>
    /// S21's arguments (tool spec 5): "keep identifier" true, so the structure of a tag stays; "keep old names" is
    /// <paramref name="keepExisting"/>, with which EPLAN numbers only tags with '?' (XML; not verified). EPLAN's
    /// InvalidScheme names the schemes it has; other EPLAN failures go back as EPLAN_OPERATION_FAILED with EPLAN's message.
    /// </summary>
    public void Renumber(string projectPath, IReadOnlyList<string>? cables, string scheme, bool keepExisting, int start, int increment)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Function[] inScope = cables is null
            ? Cables(finder)
            : [.. Cables(finder).Where(f => cables.Contains(f.Name ?? string.Empty, StringComparer.Ordinal))];
        if (inScope.Length == 0)
        {
            return;
        }

        using var service = new CableService();
        try
        {
            service.RenumberCables(inScope, scheme, keepExisting, true, start, increment);
        }
        catch (InvalidScheme ex)
        {
            throw SchemeChoice.Refused(scheme, ex.Message, "cable", ex);
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not number the cables, so nothing was numbered. metadata.eplanMessage has EPLAN's message.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }

    private static Function[] Cables(DMObjectsFinder finder)
    {
        using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
        return finder.GetFunctions(filter);
    }
}

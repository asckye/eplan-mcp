using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>EPLAN's "reassign all" for cables through <c>CableService.DoReassignWires</c> (api-notes A108): a connection gets a core template of the same potential type.</summary>
internal sealed class EplanCableReassigner : ICableReassigner
{
    public void Reassign(string projectPath, IReadOnlyList<string>? cables, bool overwrite)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var service = new CableService();
        try
        {
            if (cables is null)
            {
                service.DoReassignWires(project, overwrite, null);
                return;
            }
            using var finder = new DMObjectsFinder(project);
            using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
            foreach (Function cable in EplanNativePreconditions.Valid(finder.GetFunctions(filter)).Where(f => f.IsMainFunction && cables.Contains(f.Name ?? string.Empty, StringComparer.Ordinal)))
            { service.DoReassignWires(cable, overwrite, null); }
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not assign the cores, so nothing was committed: " + ex.Message, ex);
        }
    }
}

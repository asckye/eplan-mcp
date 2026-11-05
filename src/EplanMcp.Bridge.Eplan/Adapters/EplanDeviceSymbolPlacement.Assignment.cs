using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    public void AssignUnplacedFunction(DevicePlaceArgs args, DevicePlaceData data)
    {
        MainThread.Assert();
        long? sourceId;
        try
        {
            Function[] functions = EplanFunctionAssignment.Functions(EplanProjects.ByPath(_projectPath), data.Tag);
            Function target = functions.Single(f => f.ObjectIdentifier == _placedFunctionId);
            sourceId = functions.FirstOrDefault(f => !f.IsPlaced && f.FunctionCategory == target.FunctionCategory
                && (args.ConnectionDesignations is null || EplanFunctionAssignment.Pins(f).SequenceEqual(args.ConnectionDesignations, StringComparer.Ordinal)))?.ObjectIdentifier;
            GC.KeepAlive(functions);
        }
        catch (Exception failure)
        {
            WriteExceptionDiagnostics.Report(failure);
            data.Warning = LayoutGuard.Summary([data.Warning, "The symbol was committed, but matching unplaced functions could not be read. Inspect eplan_device_get before placing another function."]);
            return;
        }
        if (sourceId is null)
        { return; }
        EplanFunctionAssignment.Result result = EplanFunctionAssignment.Assign(_projectPath, sourceId.Value, _placedFunctionId!.Value, data.Tag);
        data.AssignedFunction = result.Assigned ? true : null;
        data.Pins = result.Pins;
        data.Functions = result.Functions ?? data.Functions;
        data.UnplacedFunctions = result.UnplacedFunctions ?? data.UnplacedFunctions;
        data.Unplaced = result.UnplacedFunctions?.Length ?? data.Unplaced;
        data.Warning = LayoutGuard.Summary([data.Warning, result.Warning]);
    }
}

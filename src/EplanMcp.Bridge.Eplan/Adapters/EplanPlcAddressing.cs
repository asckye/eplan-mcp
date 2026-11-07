using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>PLC addressing through <see cref="PlcService.ReaddressTerminals(Project, string, string, string, string, string, string, string, PlcService.Sorting)"/>; the address is FUNC_PLCADDRESS (20400), read by function category as EplanPlcReader does.</summary>
internal sealed class EplanPlcAddressing : IPlcAddressing
{
    public IReadOnlyList<PlcAddressEntry> Read(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.PLCTerminal };
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(filter)).Select(Entry)];
    }

    public void Readdress(string projectPath, PlcReaddressArgs args)
    {
        MainThread.Assert();
        try
        {
            using var service = new PlcService();
            service.ReaddressTerminals(
                EplanProjects.ByPath(projectPath),
                args.DigitalInput ?? string.Empty,
                args.DigitalOutput ?? string.Empty,
                args.AnalogInput ?? string.Empty,
                args.AnalogOutput ?? string.Empty,
                string.Empty,
                string.Empty,
                args.Cpu ?? string.Empty,
                args.Sorting switch
                {
                    "channel" => PlcService.Sorting.ChanelDesignation,
                    "graphical" => PlcService.Sorting.Graphical,
                    _ => PlcService.Sorting.ConnectionPointDesignation,
                });
        }
        catch (ArgumentException ex)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused a start address, so nothing was committed. Use the address format eplan_plc_get shows for this project: " + ex.Message, ex);
        }
        catch (BaseException ex)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused the PLC addressing, so nothing was committed: " + ex.Message, ex);
        }
    }

    private static PlcAddressEntry Entry(Function function)
    {
        using FunctionPropertyList properties = function.Properties;
        using PropertyValue address = properties.FUNC_PLCADDRESS;
        return new PlcAddressEntry
        {
            Id = function.ToStringIdentifier(),
            Tag = function.Name ?? string.Empty,
            Address = address.IsEmpty ? string.Empty : address.ToString(),
        };
    }
}

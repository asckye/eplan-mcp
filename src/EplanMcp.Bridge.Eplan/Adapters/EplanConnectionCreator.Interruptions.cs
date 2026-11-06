using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanConnectionCreator
{
    internal static void NameInterruption(Page page, FunctionBase point, string name)
    {
        // PDF redraw 2026-11-06: on a page without a higher-level function EPLAN stored +S1-L1 as =-+S1-L1; the full form starts with '='.
        string full = name.Length > 0 && name[0] == '=' ? name : "=" + name;
        point.Name = full;
        // PDF redraw 2026-11-06: a project whose interruption points keep no structure (the EES basic project) stored =-L1 as -=-L1.
        if (point.Name == "-" + full && full.IndexOfAny(['-']) is > 0 and var device)
        { point.Name = full.Substring(device); }
        EplanVisibleNames.Adjust(page, point);
    }

    internal static PinBase ReadInterruptionPin(SymbolReference point)
    {
        PinBase[] pins = point.GraphicalConnectionPoints;
        var connected = new HashSet<int>();
        // A83: graphical edges identify a symbol and its connection-point index at each end.
        foreach (var connection in point.GraphicalConnections)
        {
            if (connection.StartSymbolRef.ObjectIdentifier == point.ObjectIdentifier)
            { connected.Add(connection.StartConnectionPoint.Index); }
            if (connection.EndSymbolRef.ObjectIdentifier == point.ObjectIdentifier)
            { connected.Add(connection.EndConnectionPoint.Index); }
        }
        return pins.LastOrDefault(pin => connected.Contains(pin.Index)) ?? pins.Last();
    }
}

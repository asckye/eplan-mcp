using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanConnectionCreator
{
    internal static void NameInterruption(Page page, FunctionBase point, string name)
    {
        point.Name = name;
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

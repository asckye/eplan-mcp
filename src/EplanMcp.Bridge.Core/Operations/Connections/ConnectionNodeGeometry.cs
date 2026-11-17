using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A52/A75: bare connection symbols can join distinct pin positions inside their measured outline.</summary>
internal static class ConnectionNodeGeometry
{
    internal static SymbolConnectionPoint[] PassThroughPins(DrawingPlacement placement, Dictionary<string, SymbolConnectionPoint> pins)
    {
        // CO and T nodes are SymbolReference placements; Function subclasses must never join their device pins.
        if (placement.Kind != "SymbolReference")
        { return []; }
        string owner = FormattableString.Invariant($"{placement.Locator.Type}/{placement.Locator.Oid}/");
        SymbolConnectionPoint[] own = pins.Where(pin => pin.Key.StartsWith(owner, StringComparison.Ordinal)).Select(pin => pin.Value).ToArray();
        if (own.Length < 2 || own.Any(pin => pin.Location.Length != 2 || pin.Location.Any(n => double.IsNaN(n) || double.IsInfinity(n))))
        { return []; }
        bool measured = DrawingCoordinates.ValidBounds(placement.Bounds) && placement.Bounds!.Any(n => n != 0);
        bool contained = measured ? own.All(pin => pin.Location[0] >= placement.Bounds![0] && pin.Location[0] <= placement.Bounds[2]
                && pin.Location[1] >= placement.Bounds[1] && pin.Location[1] <= placement.Bounds[3])
            : own.All(pin => pin.Location.SequenceEqual(own[0].Location));
        return contained ? own : [];
    }
}

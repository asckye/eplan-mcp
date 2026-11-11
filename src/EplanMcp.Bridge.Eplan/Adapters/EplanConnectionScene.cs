using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanConnectionScene
{
    internal static void ReadPins(ConnectionCreatePlanner.Scene scene, IEnumerable<Placement> placements)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SymbolReference reference in EplanNativePreconditions.Valid(placements).OfType<SymbolReference>())
        {
            // A71/A86: wires and their definition points are not devices/nodes that terminate an edge. A potential point (SPECIAL/PCP)
            // is also a PotentialDefinition but starts wires (Codex review eval 2026-11-10: its wire ends were reported dangling).
            if (reference is DynamicConnectionLine or ConnectionDefinitionPoint || reference is PotentialDefinition && reference.SymbolVariant?.SymbolName != "PCP"
                || !seen.Add(reference.ToStringIdentifier()))
            { continue; }
            foreach (PinBase pin in reference.GraphicalConnectionPoints)
            {
                string key = FormattableString.Invariant($"{SnapshotCanonical.TypeOf(reference.ToStringIdentifier())}/{reference.ObjectIdentifier}/{pin.Index}");
                scene.Pins.Add(key, new() { Location = [reference.Location.X + pin.Location.X, reference.Location.Y + pin.Location.Y], Direction = pin.Direction.ToString() });
            }
        }
        // Wire feeder 2026-11-11 (api-notes A134): the inner end of a GV2P pole, a two-sided device connection point (SPECIAL/D2DCPNG),
        // at (0,-12) ends the wire to the drive, yet the page review found no pin there and reported the wire end dangling. A pin
        // that a graphical connection ends on is a pin; add the ones the symbol's own list left out.
        foreach (SymbolReference reference in EplanNativePreconditions.Valid(placements).OfType<SymbolReference>().Where(r => r is not (DynamicConnectionLine or ConnectionDefinitionPoint)))
        {
            foreach (SymbolReference.GraphicalConnection connection in reference.GraphicalConnections)
            {
                // A zero-length connection draws no wire (A72); its ends stay out, as before, so they are not reported unconnected.
                if (connection.StartSymbolRef is { } a && connection.StartConnectionPoint is { } p && connection.EndSymbolRef is { } b && connection.EndConnectionPoint is { } q
                    && a.Location.X + p.Location.X == b.Location.X + q.Location.X && a.Location.Y + p.Location.Y == b.Location.Y + q.Location.Y)
                { continue; }
                foreach ((SymbolReference end, PinBase pin) in new[] { (connection.StartSymbolRef, connection.StartConnectionPoint), (connection.EndSymbolRef, connection.EndConnectionPoint) })
                {
                    if (end is null || pin is null || end is DynamicConnectionLine or ConnectionDefinitionPoint || end is PotentialDefinition && end.SymbolVariant?.SymbolName != "PCP")
                    { continue; }
                    string key = FormattableString.Invariant($"{SnapshotCanonical.TypeOf(end.ToStringIdentifier())}/{end.ObjectIdentifier}/{pin.Index}");
                    if (!scene.Pins.ContainsKey(key))
                    { scene.Pins.Add(key, new() { Location = [end.Location.X + pin.Location.X, end.Location.Y + pin.Location.Y], Direction = pin.Direction.ToString() }); }
                }
            }
        }
    }
}

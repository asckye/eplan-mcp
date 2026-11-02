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
            // A71: a dynamic wire is an edge, not a device/node whose pins can terminate that edge.
            if (reference is DynamicConnectionLine || !seen.Add(reference.ToStringIdentifier()))
            { continue; }
            foreach (PinBase pin in reference.GraphicalConnectionPoints)
            {
                string key = FormattableString.Invariant($"{SnapshotCanonical.TypeOf(reference.ToStringIdentifier())}/{reference.ObjectIdentifier}/{pin.Index}");
                scene.Pins.Add(key, new() { Location = [reference.Location.X + pin.Location.X, reference.Location.Y + pin.Location.Y], Direction = pin.Direction.ToString() });
            }
        }
    }
}

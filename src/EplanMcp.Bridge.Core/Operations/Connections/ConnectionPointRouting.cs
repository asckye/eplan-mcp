using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A75: retain detached evidence for every connection definition point, including failed owning-route lookups.</summary>
internal static class ConnectionPointRouting
{
    internal static void Initialize(ConnectionCreatePlanner.Scene scene)
    {
        scene.PointRoutes.Clear();
        scene.PointRouteGaps.Clear();
        foreach (DrawingPlacement point in scene.Page.Placements.Where(p => p.Kind == "ConnectionDefinitionPoint"))
        { scene.PointRouteGaps[FormattableString.Invariant($"{point.Locator.Type}/{point.Locator.Oid}")] = "no connection lists it"; }
    }

    internal static void Record(ConnectionCreatePlanner.Scene scene, IEnumerable<string> owners, string? startKey, string? endKey)
    {
        double[][]? path = null;
        string? reason = null;
        if (startKey is null || endKey is null || !scene.Pins.TryGetValue(startKey, out SymbolConnectionPoint? from)
            || !scene.Pins.TryGetValue(endKey, out SymbolConnectionPoint? to))
        { reason = $"pins unresolved {startKey ?? "<missing>"}/{endKey ?? "<missing>"}"; }
        else
        {
            path = CableDefinitionPlanner.DrawnPath(scene, from.Location, to.Location, out int count);
            if (path is null)
            { reason = count == 0 ? "no drawn path" : FormattableString.Invariant($"drawn path not unique ({count} paths)"); }
        }
        foreach (string owner in owners)
        {
            if (path is null)
            {
                if (!scene.PointRoutes.ContainsKey(owner))
                { scene.PointRouteGaps[owner] = reason!; }
                continue;
            }
            if (!scene.PointRoutes.TryGetValue(owner, out List<double[][]>? routes))
            { scene.PointRoutes[owner] = routes = []; }
            routes.AddRange(path.Zip(path.Skip(1), (p, q) => new[] { p, q }));
            scene.PointRouteGaps.Remove(owner);
        }
    }
}

using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    private static string CableTag(string group) => "+S43LAB-S43W" + (group == "cable_wires" ? "2" : "1");
    private static string RunSuffix(string group, bool top, int index) => (group == "cable_wires" ? "W2" : "W") +
        (top ? "Top" : "Bottom") + Evidence.Number(index);
    private static bool RunEndpoints(Connection c, string group, int index)
    {
        string? start = c.StartPin?.ParentFunction?.Name, end = c.EndPin?.ParentFunction?.Name;
        string top = Tag(RunSuffix(group, true, index)), bottom = Tag(RunSuffix(group, false, index));
        return (start == top && end == bottom) || (end == top && start == bottom);
    }
    private Connection[] CableRuns(Page page, string group)
    {
        using var finder = new DMObjectsFinder(project);
        TraceStage("cable.runs.enumerate", new { page = page.Name, group });
        report("cable_run_inventory", CablePageData(page));
        Connection[] connections = finder.GetConnections(null).Where(c => c.Page?.ObjectIdentifier == page.ObjectIdentifier).OrderBy(WireX).ToArray();
        if (connections.Length != 3 || connections.Select(WireX).Distinct().Count() != 3 || connections.Where((c, i) => !RunEndpoints(c, group, i + 1)).Any())
        { throw new InvalidOperationException("Expected exactly three committed, generated fictional cable runs; inspect cable_run_inventory."); }
        return connections;
    }
    private void MatchCableDefinitionNames(Page page, Cable cable)
    {
        TraceStage("cable.definition_points.identify", new { cable = ObjectState(cable) });
        using var finder = new DMObjectsFinder(project);
        ConnectionDefinitionPoint[] candidates = page.AllPlacements.OfType<ConnectionDefinitionPoint>()
            .Concat((cable.SubFunctions ?? []).OfType<ConnectionDefinitionPoint>())
            .Concat(finder.GetConnections(null).Where(c => c.Page?.ObjectIdentifier == page.ObjectIdentifier).SelectMany(c => c.ConnectionDefPoints))
            .GroupBy(p => p.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()).ToArray();
        report("cable_definition_inventory", new { cableOid = cable.ObjectIdentifier, candidateCount = candidates.Length,
            points = candidates.Select(p => new { state = ObjectChanges(p), cableOid = Diagnose(() => p.CableDefinitionLine?.ObjectIdentifier) }).ToArray() });
        ConnectionDefinitionPoint[] points = candidates.Where(p => p.CableDefinitionLine?.ObjectIdentifier == cable.ObjectIdentifier).OrderBy(p => p.Location.X).ToArray();
        report("cable_definition_ownership", new { cableOid = cable.ObjectIdentifier, ownedCount = points.Length,
            ownedIds = points.Select(p => p.ObjectIdentifier).ToArray(), ownershipPendingCommitOrGenerate = points.Length == 0,
            rule = "Validate three explicit connection runs after commit; definition-point count is measured, not assumed" });
        // Installed XML, Cable.CableConnections: Name and VisibleName must match on cable and definition points.
        foreach (ConnectionDefinitionPoint point in points)
        {
            Name(point, cable.Name);
            point.VisibleName = cable.VisibleName;
            if (point.Name != cable.Name || point.VisibleName != cable.VisibleName)
            { throw new InvalidOperationException("Cable/definition-point naming association differs."); }
        }
        report("cable_definition_names_after", new { cableOid = cable.ObjectIdentifier, points = points.Select(ObjectChanges).ToArray() });
    }
    private static bool HasForeignCable(Connection connection, Cable cable) => connection.ConnectionDefPoints
        .Select(p => p.CableDefinitionLine).Any(owner => owner is not null && owner.ObjectIdentifier != cable.ObjectIdentifier);
    private static void VerifyCableCrossesRuns(Cable cable, Connection[] runs, string group)
    {
        if (runs.Where((c, i) => !RunEndpoints(c, group, i + 1)).Any()) { throw new InvalidOperationException("Unexpected cable run endpoints."); }
        var graphicalPairs = cable.GraphicalConnections.Select(c => new HashSet<long>(new[] { c.StartSymbolRef.ObjectIdentifier, c.EndSymbolRef.ObjectIdentifier })).ToArray();
        if (runs.Any(c => c.StartPin?.ParentFunction is not Function a || c.EndPin?.ParentFunction is not Function b ||
            !graphicalPairs.Any(pair => pair.SetEquals(new[] { a.ObjectIdentifier, b.ObjectIdentifier }))))
        { throw new InvalidOperationException("Cable graphical intersections do not cover the three explicit logical runs."); }
    }
}

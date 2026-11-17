using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed class EplanPlacementRemover : IPlacementRemover
{
    public Action Prepare(string project, string page, IReadOnlyList<DrawingLocator> items)
    {
        MainThread.Assert();
        _ = ReadSelection();
        return () => { foreach (Placement placement in ReadSelection()) { OperationContext.Current.ThrowIfCancellationRequested(); placement.Remove(); } };

        Placement[] ReadSelection()
        {
            using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
            Placement[] all = EplanDeviceReader.FindPage(finder, page)?.AllPlacements
                ?? throw new BridgeErrorException(ErrorReason.PlanStale, "The page disappeared; read it again.");
            return Select(all, items);
        }
    }

    private static Placement[] Select(Placement[] all, IReadOnlyList<DrawingLocator> items)
    {
        var members = new HashSet<string>(all.OfType<Block>().SelectMany(Members).Select(p => p.ToStringIdentifier()), StringComparer.Ordinal);
        Placement[] selected = items.Select(l => all.FirstOrDefault(p => p.ObjectIdentifier == l.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == l.Type)
            ?? throw new BridgeErrorException(ErrorReason.PlanStale, "A placement disappeared; read the page again.")).ToArray();
        foreach (Placement placement in selected)
        {
            if (members.Contains(placement.ToStringIdentifier()) || placement is PropertyPlacement)
            { throw WriteArguments.Invalid(["Block members and property text cannot be deleted separately."]); }
            if (!PlacementDeleteOperation.Allowed(placement.GetType().Name))
            { throw WriteArguments.Invalid([$"Cannot delete {placement.GetType().Name}: devices, connections and blocks are excluded."]); }
        }
        return selected;
    }

    private static Placement[] Members(Block block)
    {
        try
        { return block.SubPlacements; }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        { WriteExceptionDiagnostics.Report(ex); return []; }
    }
}

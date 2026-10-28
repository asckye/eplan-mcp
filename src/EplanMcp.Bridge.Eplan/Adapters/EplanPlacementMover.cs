using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A54: line endpoints move together; A53/A57 property offsets are never assigned or deduplicated by OID.</summary>
internal sealed class EplanPlacementMover : IPlacementMover
{
    public ConnectionCreatePlanner.Scene Read(string project, string page) => new EplanConnectionCreator(readSymbolBounds: false).Read(project, page);
    public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(scene.Project.Path));
        Page page = EplanDeviceReader.FindPage(finder, scene.Page.Page)!;
        var roots = moves.Where(m => m["parent"]!.Type == JTokenType.Null).Select(m =>
        {
            DrawingLocator locator = m["locator"]!.ToObject<DrawingLocator>()!;
            Placement root = page.AllPlacements.Single(p => p is not PropertyPlacement && p.ObjectIdentifier == locator.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == locator.Type);
            if (root.Group is not null)
            { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.CapabilityMissing, "Grouped placements cannot be moved.")); }
            return (Root: root, Before: root.Location, After: new PointD((double)m["after"]![0]!, (double)m["after"]![1]!));
        }).ToArray();
        return () =>
        {
            foreach (var item in roots)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if (item.Before.Equals(item.After))
                { continue; }
                if (item.Root is Line line)
                {
                    PointD start = line.StartPoint, end = line.EndPoint;
                    PointD Shift(PointD point) => new(point.X + item.After.X - item.Before.X, point.Y + item.After.Y - item.Before.Y);
                    line.StartPoint = Shift(start);
                    line.EndPoint = Shift(end);
                    if (!line.StartPoint.Equals(Shift(start)) || !line.EndPoint.Equals(Shift(end)))
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Line endpoints did not follow the move."); }
                }
                else
                { item.Root.Location = item.After; }
            }
        };
    }
}

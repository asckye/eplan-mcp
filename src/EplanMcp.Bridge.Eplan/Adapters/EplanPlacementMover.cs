using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A54: line endpoints move together; A57 property text is selected by parent, property and collection slot.</summary>
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
            Placement root = EplanNativePreconditions.Valid(page.AllPlacements).ToArray().Single(p => p is not PropertyPlacement && p.ObjectIdentifier == locator.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == locator.Type);
            EplanNativePreconditions.Writable(root);
            if (root.Group is not null)
            { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.CapabilityMissing, "Grouped placements cannot be moved.")); }
            return (Root: root, Before: root.Location, After: new PointD((double)m["after"]![0]!, (double)m["after"]![1]!));
        }).ToArray();
        var texts = moves.Where(m => m["angle"] is not null).Select(m =>
        {
            DrawingLocator parent = m["parent"]!.ToObject<DrawingLocator>()!;
            Placement owner = page.AllPlacements.Single(p => p is not PropertyPlacement && p.ObjectIdentifier == parent.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == parent.Type);
            PropertyPlacement text = (owner is SymbolReference symbol ? symbol.PropertyPlacements : ((IPropertyPlacementsContainer)owner).PropertyPlacements)[(int)m["propertyIndex"]!];
            using AnyPropertyId id = text.DisplayedProperty;
            if (id.AsInt != (int)m["propertyId"]! || text.Parent.ObjectIdentifier != owner.ObjectIdentifier)
            { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale, "Property placement selection changed; refresh drawings.")); }
            if (m["style"] is JObject style)
            { EplanPlacementStyle.Validate(EplanProjects.ByPath(scene.Project.Path), (int?)style["layer"], (string?)style["alignment"]); }
            return (Owner: owner, Row: m);
        }).ToArray();
        return () =>
        {
            foreach (var group in texts.GroupBy(t => t.Owner.ToStringIdentifier(), StringComparer.Ordinal))
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                Placement owner = group.First().Owner;
                // A57: default texts belong to the read-only symbol library. Copy inside the write scope,
                // then resolve the local copies; the preflight objects still refer to the library.
                if (owner is SymbolReference symbol && !symbol.UseLocalPropertyPlacements)
                { symbol.SwitchLocalPropertyPlacements(true); }
                foreach (var item in group)
                {
                    OperationContext.Current.ThrowIfCancellationRequested();
                    PropertyPlacement text = (owner is SymbolReference local ? local.PropertyPlacements : ((IPropertyPlacementsContainer)owner).PropertyPlacements)[(int)item.Row["propertyIndex"]!];
                    using AnyPropertyId id = text.DisplayedProperty;
                    if (id.AsInt != (int)item.Row["propertyId"]! || text.Parent.ObjectIdentifier != owner.ObjectIdentifier)
                    { throw new BridgeErrorException(ErrorReason.PlanStale, "Local property placement selection changed; refresh drawings."); }
                    if (text.IsDocked)
                    { text.UndockText(); }
                    if (item.Row["style"] is JObject style)
                    {
                        if (style["height_mm"] is { } height)
                        { text.Height = (double)height; }
                        if (style["visible"] is { } visible)
                        { text.IsVisible = (bool)visible; }
                        if (style["alignment"] is { } alignment)
                        { text.Justification = (TextBase.JustificationType)Enum.Parse(typeof(TextBase.JustificationType), alignment.ToString()); }
                        EplanPlacementStyle.Apply(text, EplanProjects.ByPath(scene.Project.Path), (int?)style["layer"], style["pen"]?.ToObject<Dictionary<string, double>>());
                    }
                    text.Rotation = (double)item.Row["angle"]! == PropertyPlacementMoves.FromLayerAngle ? PropertyPlacementMoves.FromLayerAngle : (double)item.Row["angle"]! * Math.PI / 180;
                    text.Location = new PointD((double)item.Row["after"]![0]! - owner.Location.X, (double)item.Row["after"]![1]! - owner.Location.Y);
                }
            }
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

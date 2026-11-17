using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanGraphicEditor
{
    public Action PrepareEdit(JObject input, JObject before, JObject plan) => () =>
    {
        MainThread.Assert();
        string project = (string)before["scene"]!["project"]!["path"]!, pageName = (string)input["page"]!;
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        Page page = EplanDeviceReader.FindPage(finder, pageName)!;
        var all = new Dictionary<string, Placement>(StringComparer.Ordinal);
        void Add(Placement placement)
        {
            if (!placement.IsValid)
            { OperationContext.Current.Warn("An invalid placement was skipped while resolving the graphic edit."); return; }
            if (placement is PropertyPlacement || all.ContainsKey(placement.ToStringIdentifier()))
            { return; }
            all.Add(placement.ToStringIdentifier(), placement);
            if (placement.Group is { } group)
            { Add(group); }
            if (placement is Group container)
            { foreach (Placement child in container.SubPlacements) { Add(child); } }
        }
        foreach (Placement placement in EplanNativePreconditions.Valid(page.AllPlacements))
        { Add(placement); }
        Placement Resolve(JToken locator) => all.Values.SingleOrDefault(p => p.ObjectIdentifier == (long)locator["oid"]! && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == (int)locator["type"]!)
            ?? throw WriteArguments.NotStarted(new(ErrorReason.PlanStale, "The selected placement is no longer valid; refresh eplan_object_find and preview again."));
        Placement[] roots = input["locators"]!.Select(Resolve).ToArray();
        foreach (Placement root in roots)
        { EplanNativePreconditions.Writable(root); }
        using var edit = new Edit();
        PointD Point(JToken p) => new((double)p[0]!, (double)p[1]!);
        string operation = (string)input["operation"]!;
        if (operation == "group")
        {
#pragma warning disable CA2000 // The created group is owned by the project, including after rollback.
            var group = new Group();
#pragma warning restore CA2000
            group.Create(roots);
        }
        else if (operation == "ungroup")
        {
            Group[] groups = roots.Select(p => p as Group ?? p.Group).Where(g => g is not null).Cast<Group>().GroupBy(g => g.ToStringIdentifier()).Select(g => g.First()).ToArray();
            foreach (Group group in groups)
            {
                group.DeepUnGroup(true);
                // Live 2026-11-05: DeepUnGroup leaves the emptied group on the page.
                if (group.IsValid)
                { group.Remove(); }
            }
        }
        else if (operation == "explode_block")
        { foreach (Block block in roots.Cast<Block>()) { block.BreakUp(); } }
        else if (operation == "dock_text")
        {
            var text = (TextBase)roots[0];
            text.DockText((TextBase)roots[1]);
            if (input["dock_direction"] is { } direction)
            {
                text.DockingDirection = (string)direction! switch
                {
                    "bottom" => TextBase.TextDockingDirection.DockToBottom,
                    "top" => TextBase.TextDockingDirection.DockToTop,
                    "left" => TextBase.TextDockingDirection.DockToLeft,
                    "right" => TextBase.TextDockingDirection.DockToRight,
                    _ => throw new InvalidOperationException("Unexpected validated dock direction.")
                };
            }
        }
        else if (operation == "cut_off")
        {
            GraphicalPlacement[]? modified = edit.CutOff(page, Point(input["point_mm"]!));
            plan["cutOffResult"] = modified is null ? null : new JArray(modified.Where(p => p.IsValid).Select(p => new JObject { ["type"] = SnapshotCanonical.TypeOf(p.ToStringIdentifier()), ["oid"] = p.ObjectIdentifier }));
            plan["removed"] = modified is { Length: 0 };
        }
        else
        {
            GraphicalPlacement? source = input["source"] is { } locator ? (GraphicalPlacement)Resolve(locator) : null;
            foreach (Placement root in roots)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if (plan["skipped"] is JArray skipped && skipped.Any(l => (long)l["oid"]! == root.ObjectIdentifier && (int)l["type"]! == SnapshotCanonical.TypeOf(root.ToStringIdentifier())))
                { continue; }
                if (operation == "scale")
                { root.Scale((double)input["scale_x"]!, (double)input["scale_y"]!, Point(input["origin_mm"]!)); }
                else if (operation == "order")
                {
                    if (input["drawing_order"] is { } order)
                    { root.DrawingOrder = checked((short)(int)order); }
                    else if ((string?)input["to"] == "front")
                    { edit.BringToFront(root); }
                    else
                    { edit.SendToBack(root); }
                }
                else if (operation == "undock_text")
                { ((TextBase)root).UndockText(); }
                else if (operation == "corner_radius")
                { var rectangle = (Rectangle)root; rectangle.HasChamfers = (double)input["radius_mm"]! > 0; if (rectangle.HasChamfers) { rectangle.ChamferRadius = (double)input["radius_mm"]!; } }
                else if (operation == "copy_format")
                { var graphic = (GraphicalPlacement)root; graphic.Pen = source!.Pen; graphic.Layer = source.Layer; }
                else
                {
                    JToken predicted = plan["predicted"]!.Single(p => (long)p["locator"]!["oid"]! == root.ObjectIdentifier && (int)p["locator"]!["type"]! == SnapshotCanonical.TypeOf(root.ToStringIdentifier()));
                    if (root is Line line)
                    { line.StartPoint = Point(predicted["points"]![0]!); line.EndPoint = Point(predicted["points"]![1]!); }
                    else if (operation == "stretch")
                    { var rectangle = (Rectangle)root; JToken bounds = predicted["bounds"]!; rectangle.SetArea(new PointD((double)bounds[0]!, (double)bounds[1]!), new PointD((double)bounds[2]!, (double)bounds[3]!)); }
                    else if (root is Group group)
                    { JToken at = predicted["location"]!; group.Move(new PointD((double)at[0]! - root.Location.X, (double)at[1]! - root.Location.Y)); }
                    else
                    { root.Location = Point(predicted["location"]!); }
                }
            }
        }
        if (plan["created"] is JArray { Count: > 0 } created)
        {
            var source = (GraphicalPlacement)roots[0];
            PlacementText[] items = created.Select(p => p.ToObject<PlacementText>(BridgeJson.Serializer)!).ToArray();
            foreach (PlacementText item in items)
            { item.Layer = source.LayerId; item.Pen = PenState(source.Pen).ToObject<Dictionary<string, double>>(); }
            new EplanPlacementTextWriter().CreateBatch(project, pageName, items);
        }
    };
}

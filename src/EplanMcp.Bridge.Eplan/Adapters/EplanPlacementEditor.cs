using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;
/// <summary>A39/A54/A57: borrowed roots and children are never disposed. New behaviour: PlacementEditSpikeTests.</summary>
internal sealed partial class EplanPlacementEditor : IPlacementEditor
{
    public ConnectionCreatePlanner.Scene Read(string project, string page) => new EplanPlacementMover().Read(project, page);
    public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => new EplanPlacementMover().Prepare(scene, moves);
    public JObject Inspect(string project, string page, bool symbolEdit = false)
    {
        MainThread.Assert();
        if (symbolEdit)
        { return InspectSymbols(project, page); }
        var scene = Read(project, page);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(scene.Project.Path));
        Placement[] placements = EplanDeviceReader.FindPage(finder, page)!.AllPlacements;
        JToken? End(Pin? pin) => pin?.ParentFunction is not { } owner ? null : new JObject { ["type"] = SnapshotCanonical.TypeOf(owner.ToStringIdentifier()), ["oid"] = owner.ObjectIdentifier, ["pin"] = pin.Name, ["function"] = owner.Name };
        JObject State(EplanMcp.Contracts.DrawingPlacement row, Placement native, bool child)
        {
            JObject state = (JObject)Bridge.Core.Protocol.BridgeJson.FromObject(row);
            state.Remove("parent");
            state.Remove("locator");
            state["angle"] = native is TextBase text ? text.Rotation == -16002 ? null : PlacementEditPlanner.Normalize(text.Rotation) : native is Rectangle rectangle ? PlacementEditPlanner.Normalize(rectangle.Angle) : null;
            if (native is Line line)
            { state["points"] = new JArray(new JArray(line.StartPoint.X, line.StartPoint.Y), new JArray(line.EndPoint.X, line.EndPoint.Y)); }
            if (child)
            { return state; }
            string kind = native.GetType().Name.ToLowerInvariant();
            var identity = EplanObjectReader.Describe(native, kind);
            state["drawing"] = Bridge.Core.Protocol.BridgeJson.FromObject(row.Locator);
            state["locator"] = Bridge.Core.Protocol.BridgeJson.FromObject(new EplanMcp.Contracts.DrawingLocator { Project = scene.Project.Path, Page = page, Type = identity.Type, Oid = identity.Oid, Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(identity)) });
            string prefix = PlacementEditPlanner.Id(state["locator"]!) + "/";
            state["pins"] = JObject.FromObject(scene.Pins.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(p => p.Key.Substring(prefix.Length), p => p.Value), Bridge.Core.Protocol.BridgeJson.Serializer);
            PropertyPlacement[] children = native is SymbolReference symbol ? symbol.PropertyPlacements : [];
            var rows = scene.Page.Placements.Where(p => p.Parent?.Type == row.Locator.Type && p.Parent.Oid == row.Locator.Oid).ToArray();
            if (rows.Length != children.Length)
            { throw new BridgeErrorException(EplanMcp.Contracts.ErrorReason.CapabilityMissing, "Property child inventory is incomplete."); }
            state["children"] = new JArray(rows.Select((p, i) => State(p, children[i], true)));
            return state;
        }
        return new JObject
        {
            ["scene"] = Bridge.Core.Protocol.BridgeJson.FromObject(scene),
            ["objects"] = new JArray(scene.Page.Placements.Where(p => p.Parent is null).Select(row => State(row, placements.Single(p => p.ObjectIdentifier == row.Locator.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == row.Locator.Type), false))),
            ["functions"] = new JArray(finder.GetFunctions(null).OrderBy(f => f.ObjectIdentifier).Select(f => new JObject { ["type"] = SnapshotCanonical.TypeOf(f.ToStringIdentifier()), ["oid"] = f.ObjectIdentifier, ["name"] = f.Name, ["page"] = f.Page?.Name, ["main"] = f.IsMainFunction })),
            ["connections"] = new JArray(finder.GetConnections(null).OrderBy(c => c.ObjectIdentifier).Select(c => new JObject { ["type"] = SnapshotCanonical.TypeOf(c.ToStringIdentifier()), ["oid"] = c.ObjectIdentifier, ["page"] = EplanConnectionReader.PageOf(c)?.Name, ["start"] = c is Connection3D ? null : End(c.StartPin), ["end"] = c is Connection3D ? null : End(c.EndPin) }))
        };
    }
    public Action PrepareEdit(JObject input, JObject before)
    {
        if ((string?)input["operation"] == "set_variant")
        { return () => EditSymbols(input, before); }
        using var finder = new DMObjectsFinder(EplanProjects.ByPath((string)before["scene"]!["project"]!["path"]!));
        Page page = EplanDeviceReader.FindPage(finder, (string)input["page"]!)!;
        Placement[] roots = input["locators"]!.Select(l => page.AllPlacements.Single(p => p is not PropertyPlacement && p.ObjectIdentifier == (long)l["oid"]! && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == (int)l["type"]!)).ToArray();
        return () =>
        {
            using var edit = new Edit();
            foreach (Placement root in roots)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                PointD Point(JToken p) => new((double)p[0]!, (double)p[1]!);
                string op = (string)input["operation"]!;
                if (op == "delete")
                { root.Remove(); }
                else if (op == "copy")
                {
                    for (int n = 0; n < ((int?)input["copies"] ?? 1); n++)
                    { OperationContext.Current.ThrowIfCancellationRequested(); PointD offset = input["offset_mm"] is { } d ? Point(d) : new PointD((double)input["target_mm"]![0]! - roots[0].Location.X, (double)input["target_mm"]![1]! - roots[0].Location.Y); PointD step = input["step_mm"] is { } s ? Point(s) : new PointD(0, 0); Placement copy = root.CopyTo(page); if (copy is Line line) { line.StartPoint = new PointD(line.StartPoint.X + offset.X + n * step.X, line.StartPoint.Y + offset.Y + n * step.Y); line.EndPoint = new PointD(line.EndPoint.X + offset.X + n * step.X, line.EndPoint.Y + offset.Y + n * step.Y); } else { copy.Location = new PointD(root.Location.X + offset.X + n * step.X, root.Location.Y + offset.Y + n * step.Y); } }
                }
                else if (op == "rotate")
                { edit.Rotate([root], input["pivot_mm"] is { } p ? Point(p) : root.Location, (int)input["quarter_turns"]! * Math.PI / 2); }
                else
                { double a = (double)input["coordinate_mm"]!; bool vertical = (string)input["axis"]! == "vertical"; edit.Mirror([root], new PointD(vertical ? a : 0, vertical ? 0 : a), new PointD(vertical ? a : 1, vertical ? 1 : a), false); }
            }
        };
    }
}

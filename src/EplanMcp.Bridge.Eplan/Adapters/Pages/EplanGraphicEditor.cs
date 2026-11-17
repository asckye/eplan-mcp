using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Detached graphic evidence; project-owned objects never survive the active read/write scope.</summary>
internal sealed partial class EplanGraphicEditor : IGraphicEditor
{
    public JObject Inspect(string project, string page)
    {
        MainThread.Assert();
        OpenProject target = ProjectResolver.Resolve(new EplanProjectCatalog().ListOpenProjects(), project);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(target.Path));
        Page nativePage = EplanDeviceReader.FindPage(finder, page) ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "Select an existing page with eplan_page_list.");
        ConnectionCreatePlanner.Scene scene = Optional(() => new EplanConnectionCreator(false).Read(target.Path, page)) ?? new ConnectionCreatePlanner.Scene
        {
            Project = target,
            Page = new EplanDrawingReader().Read(target.Path, page)!,
            PageType = nativePage.PageType.ToString(),
            Complete = false,
            Layout = EplanLayout.Config()
        };
        var placements = new Dictionary<string, Placement>(StringComparer.Ordinal);
        void Visit(Placement native)
        {
            if (native is PropertyPlacement || placements.ContainsKey(native.ToStringIdentifier()))
            { return; }
            placements.Add(native.ToStringIdentifier(), native);
            if (native.Group is { } group)
            { Visit(group); }
            if (native is Group grouping)
            { foreach (Placement child in Optional(() => grouping.SubPlacements) ?? []) { Visit(child); } }
        }
        foreach (Placement native in EplanNativePreconditions.Valid(Optional(() => nativePage.AllPlacements) ?? []))
        { Visit(native); }
        var objects = new JArray(placements.Values.OrderBy(p => p.ToStringIdentifier(), StringComparer.Ordinal).Select(p => Optional(() => State(p, scene, false))).Where(p => p is not null));
        scene.Complete &= objects.Count == placements.Count;
        JToken? End(Pin? pin) => pin?.ParentFunction is not { } owner ? null : new JObject { ["type"] = SnapshotCanonical.TypeOf(owner.ToStringIdentifier()), ["oid"] = owner.ObjectIdentifier, ["pin"] = EplanPins.Name(pin), ["function"] = owner.Name };
        return new JObject
        {
            ["scene"] = BridgeJson.FromObject(scene),
            ["objects"] = objects,
            ["functions"] = Optional(() => new JArray(finder.GetFunctions(null).OrderBy(f => f.ObjectIdentifier).Select(f => new JObject { ["type"] = SnapshotCanonical.TypeOf(f.ToStringIdentifier()), ["oid"] = f.ObjectIdentifier, ["name"] = f.Name, ["page"] = f.Page?.Name, ["main"] = f.IsMainFunction }))),
            ["connections"] = Optional(() => new JArray(finder.GetConnections(null).OrderBy(c => c.ObjectIdentifier).Select(c => new JObject { ["type"] = SnapshotCanonical.TypeOf(c.ToStringIdentifier()), ["oid"] = c.ObjectIdentifier, ["page"] = EplanConnectionReader.PageOf(c)?.Name, ["start"] = c is Connection3D ? null : End(c.StartPin), ["end"] = c is Connection3D ? null : End(c.EndPin) })))
        };
    }

    private static JObject State(Placement native, ConnectionCreatePlanner.Scene scene, bool blockMember, int childIndex = -1)
    {
        DrawingPlacement? row = blockMember ? null : scene.Page.Placements.FirstOrDefault(p => p.Locator.Type == SnapshotCanonical.TypeOf(native.ToStringIdentifier()) && p.Locator.Oid == native.ObjectIdentifier && (childIndex < 0 ? p.Parent is null : p.Properties.TryGetValue("property_index", out string index) && index == childIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var state = row is null ? new JObject { ["kind"] = native.GetType().Name, ["properties"] = new JObject() } : (JObject)BridgeJson.FromObject(row);
        state["kind"] = native is PropertyPlacement ? nameof(PropertyPlacement) : native.GetType().Name;
        state.Remove("parent");
        state["drawing"] = row is null ? null : BridgeJson.FromObject(row.Locator);
        state["locator"] = Locator(native, scene);
        state["nativeKind"] = native.GetType().Name;
        state["graphical"] = native is GraphicalPlacement;
        state["blockMember"] = blockMember;
        state["group"] = Optional(() => native.Group is { } group ? Locator(group, scene) : null);
        state["drawingOrder"] = Optional(() => (int?)native.DrawingOrder);
        state["location"] = row is not null ? BridgeJson.FromObject(row.Location) : Optional(() => new JArray(native.Location.X, native.Location.Y));
        state["bounds"] = row is not null ? BridgeJson.FromObject(row.Bounds) : Optional(() => new JArray(native.GetBoundingBox().SelectMany(p => new[] { p.X, p.Y })));
        state["angle"] = Optional(() => native is Rectangle rectangle ? (double?)rectangle.Angle : native is TextBase text && text.Rotation != -16002 ? text.Rotation : null);
        state["points"] = Optional(() => native switch
        {
            Line line => new JArray(new JArray(line.StartPoint.X, line.StartPoint.Y), new JArray(line.EndPoint.X, line.EndPoint.Y)),
            PolyLine line => new JArray(Enumerable.Range(0, line.NumberOfPoints).Select(i => new JArray(line.GetPointAt(i).X, line.GetPointAt(i).Y))),
            _ => null
        });
        state["docked"] = Optional(() => native is TextBase text ? (bool?)text.IsDocked : null);
        state["dockingDirection"] = Optional(() => (native as TextBase)?.DockingDirection.ToString());
        state["hasChamfers"] = Optional(() => native is Rectangle rectangle ? (bool?)rectangle.HasChamfers : null);
        state["cornerRadius"] = Optional(() => native is Rectangle rectangle ? (double?)rectangle.ChamferRadius : null);
        state["layer"] = Optional(() => native is GraphicalPlacement graphic ? (int?)graphic.LayerId : null);
        state["pen"] = Optional(() => native is GraphicalPlacement graphic ? PenState(graphic.Pen) : null);
        state["members"] = Optional(() => native is Group group ? new JArray(group.SubPlacements.Select(p => Locator(p, scene))) : new JArray());
        state["memberStates"] = Optional(() => !blockMember && native is Block block ? new JArray(block.SubPlacements.Select(p => State(p, scene, true))) : new JArray());
        state["children"] = Optional(() => native is SymbolReference symbol ? new JArray(symbol.PropertyPlacements.Select((p, i) => State(p, scene, blockMember, i))) : new JArray());
        if (blockMember)
        {
            state["relativeLocation"] = state["location"]?.DeepClone();
            state["relativeBounds"] = state["bounds"]?.DeepClone();
            state["relativePoints"] = state["points"]?.DeepClone();
            state["relativeAngle"] = state["angle"]?.DeepClone();
            state["location"] = null;
            state["bounds"] = null;
            state["points"] = null;
            state["angle"] = null;
            state["unverified"] = "Block member coordinates are relative; absolute geometry remains unverified.";
        }
        return state;
    }

    private static JObject Locator(Placement native, ConnectionCreatePlanner.Scene scene)
    {
        SnapshotObject identity = EplanObjectReader.Describe(native, native.GetType().Name.ToLowerInvariant());
        return (JObject)BridgeJson.FromObject(new DrawingLocator { Project = scene.Project.Path, Page = scene.Page.Page, Type = identity.Type, Oid = identity.Oid, Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(identity)) });
    }

    private static JObject PenState(Pen pen) => new() { ["color"] = pen.ColorId, ["width_mm"] = pen.Width, ["style"] = pen.StyleId, ["pattern_length"] = pen.PatternLength, ["line_end"] = pen.LineEndType };

    private static T? Optional<T>(Func<T> read)
    {
        try
        { return read(); }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        { WriteExceptionDiagnostics.Report(ex); OperationContext.Current.Warn("Some graphic evidence is unreadable; its verification remains incomplete."); return default; }
    }
}

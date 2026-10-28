using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A52/S40: borrowed objects stay inside one main-thread request; discovery precedes the write wrapper.</summary>
internal sealed class EplanConnectionCreator : IConnectionCreator
{
    public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => new EplanSymbolReader().Read(projectPath, args);
    private static Page Page(string project, string page)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        return EplanDeviceReader.FindPage(finder, page) ?? throw new BridgeErrorException(ErrorReason.InvalidArgument, "Select an existing Circuit page with eplan_page_list.");
    }
    private static string Key(SymbolReference owner, int index) => FormattableString.Invariant($"{SnapshotCanonical.TypeOf(owner.ToStringIdentifier())}/{owner.ObjectIdentifier}/{index}");
    private static double[] XY(PointD point) => [point.X, point.Y];
    private static IEnumerable<SymbolVariant> Variants(string project) => EplanProjects.ByPath(project).SymbolLibraries.Where(l => l.Name == "SPECIAL")
        .SelectMany(l => l.Symbols).Where(s => s.Name is "CO" or "TLRO" or "TLRU" or "TOUL" or "TOUR" or "PCP" or "BPF").SelectMany(s => s.Variants);
    public ConnectionCreatePlanner.Scene Read(string project, string page)
    {
        OpenProject target = ProjectResolver.Resolve(new EplanProjectCatalog().ListOpenProjects(), project);
        if (target.ReadOnly)
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Select a writable project."); }
        Page native = Page(target.Path, page);
        PageDrawingGeometry geometry = new EplanDrawingReader().Read(target.Path, page)!;
        geometry.Project = target.Path;
        var scene = new ConnectionCreatePlanner.Scene { Project = target, Page = geometry, PageType = native.PageType.ToString(), Complete = true, Graphical = Graphical(target.Path, page) };
        foreach (Placement placement in native.AllPlacements)
        {
            if (!geometry.Placements.Any(p => p.Locator.Type == SnapshotCanonical.TypeOf(placement.ToStringIdentifier()) && p.Locator.Oid == placement.ObjectIdentifier))
            { scene.Complete = false; }
            if (placement is SymbolReference reference)
            {
                foreach (PinBase pin in reference.GraphicalConnectionPoints)
                { scene.Pins.Add(Key(reference, pin.Index), new() { Location = [reference.Location.X + pin.Location.X, reference.Location.Y + pin.Location.Y], Direction = pin.Direction.ToString() }); }
            }
        }
        foreach (SymbolVariant variant in Variants(target.Path))
        { scene.SymbolBounds.Add(FormattableString.Invariant($"{variant.SymbolName}/{variant.VariantNr}"), Outline(variant)); }
        scene.Logical = Endpoints(target.Path, page).Select(p => new ConnectionRecord { StartDevice = p[0], EndDevice = p[1] }).ToList();
        return scene;
    }
    public System.Action Prepare(string project, string page, JArray symbols)
    {
        AssertCurrent(project);
        Page target = Page(project, page);
        SymbolVariant[] variants = Variants(project).ToArray();
        var selected = symbols.Select(s => (Row: s, Variant: variants.Single(v => v.SymbolName == (string)s["symbol"]!["name"]! && v.VariantNr == (int)s["symbol"]!["variant"]!))).ToArray();
        return () =>
        {
            foreach (var item in selected)
            {
                SymbolReference placed = SymbolReference.Create(item.Variant, target);
                if (item.Row["interruption"] is { } interruption)
                {
                    if (placed is not InterruptionPoint point)
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "BPF did not create an interruption point."); }
                    point.Name = (string)interruption["name"]!;
                    if (point.Name != (string)interruption["name"]!)
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Interruption business name readback differs; supply the exact full name."); }
                }
                placed.Location = new PointD((double)item.Row["position"]![0]!, (double)item.Row["position"]![1]!);
                if (placed is InterruptionPoint)
                {
                    using AnyPropertyId id = 24300;
#pragma warning disable CA2000 // A39/A55: EPLAN owns the native property placement, also on Abort.
                    var display = new PropertyPlacement();
#pragma warning restore CA2000
                    display.Create(placed, id);
                    display.Location = new PointD(4 * target.GridSize, 0);
                    display.IsVisible = true;
                    display.Height = 2.5;
                    display.Justification = TextBase.JustificationType.BaseLineLeft;
                }
                double[][] pins = placed.GraphicalConnectionPoints.Select(p => XY(p.Location)).ToArray();
                SymbolConnectionPoint[] expected = item.Row["symbol"]!["connectionPoints"]!.ToObject<SymbolConnectionPoint[]>()!;
                if (!XY(placed.Location).SequenceEqual(item.Row["position"]!.Values<double>()) || placed.SymbolVariant.SymbolName != item.Variant.SymbolName
                    || placed.SymbolVariant.VariantNr != item.Variant.VariantNr || pins.Length != expected.Length
                    || pins.Where((p, i) => !p.SequenceEqual(expected[i].Location)).Any()
                    || placed.GraphicalConnectionPoints.Where((p, i) => p.Direction.ToString() != expected[i].Direction).Any()
                    || !OutlineMatches(placed.GetBoundingBox().SelectMany(XY).ToArray(), item.Row["bounds"]!.Values<double>().ToArray(), XY(placed.Location)))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, FormattableString.Invariant(
                        $"Placed symbol geometry differs from the plan: {item.Variant.SymbolName}/{item.Variant.VariantNr} outline {string.Join(",", placed.GetBoundingBox().SelectMany(XY))}, pins {string.Join(";", pins.Select(p => string.Join(",", p)))}."));
                }
            }
        };
    }
    private sealed class PlacementFactory(string project) : ITransactionScopeFactory
    { public ITransactionScope Begin(string undoDescription) => new FunctionTransactionScope(EplanProjects.ByPath(project), undoDescription); }
    public ITransactionScopeFactory PlacementTransactions(string project) => new PlacementFactory(project);

    public JObject Interruptions(string project, IReadOnlyList<string> pages)
    {
        var snapshots = new JArray();
        var points = new JArray();
        foreach (string page in pages)
        {
            Page native = Page(project, page);
            ConnectionCreatePlanner.Scene scene = Read(project, page);
            if (!scene.Complete || scene.PageType != "Circuit" || scene.Page.PageSize is null || scene.Page.Placements.Count > 10000)
            { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Read a complete Circuit counterpart page within 10000 placements."); }
            scene.Pins.Clear();
            foreach (Function function in native.AllPlacements.OfType<Function>())
            { foreach (PinBase pin in function.GraphicalConnectionPoints) { scene.Pins.Add(Key(function, pin.Index), new() { Location = [function.Location.X + pin.Location.X, function.Location.Y + pin.Location.Y], Direction = pin.Direction.ToString() }); } }
            snapshots.Add(new JObject { ["page"] = page, ["drawing"] = BridgeJson.FromObject(scene.Page), ["pins"] = BridgeJson.FromObject(scene.Pins), ["graphical"] = JArray.FromObject(scene.Graphical), ["logical"] = BridgeJson.FromObject(scene.Logical) });
            foreach (InterruptionPoint point in native.AllPlacements.OfType<InterruptionPoint>())
            {
                // A55: acquire once, release before returning; no wrapper survives into Abort or compensation.
                using InterruptionPointPropertyList properties = point.Properties;
                using PropertyValue reference = properties[(Properties.InterruptionPoint)24300];
                PinBase pin = point.GraphicalConnectionPoints.Single();
                points.Add(new JObject
                {
                    ["page"] = page,
                    ["name"] = point.Name,
                    ["oid"] = point.ObjectIdentifier,
                    ["position"] = new JArray(XY(point.Location)),
                    ["pin"] = new JArray(point.Location.X + pin.Location.X, point.Location.Y + pin.Location.Y),
                    ["direction"] = pin.Direction.ToString(),
                    ["crossReference"] = reference.IsEmpty ? null : reference.ToString(),
                    // Live T7b: CrossReferencedObjectsAll includes the point itself.
                    ["counterparts"] = new JArray(point.CrossReferencedObjectsAll.Where(peer => peer.ObjectIdentifier != point.ObjectIdentifier).Select(peer => peer is InterruptionPoint other
                        ? new JObject { ["page"] = other.Page.Name, ["name"] = other.Name, ["oid"] = other.ObjectIdentifier }
                        : throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Unexpected native interruption counterpart type.")))
                });
            }
        }
        return new JObject { ["pages"] = snapshots, ["points"] = points };
    }
    /// <summary>
    /// A planned point outline means the library variant had none (Outline); the placed connection symbol must then be
    /// small (≤ 10 mm, live T3b: CO 2.5, T-node 3 × 4.5, PCP 2 × 5.25) and contain its insertion point.
    /// </summary>
    private static bool OutlineMatches(double[] actual, double[] planned, double[] position) =>
        planned[0] == planned[2] && planned[1] == planned[3]
            ? actual[0] <= position[0] && position[0] <= actual[2] && actual[1] <= position[1] && position[1] <= actual[3]
                && actual[2] - actual[0] <= 10 && actual[3] - actual[1] <= 10
            : actual.SequenceEqual(planned);
    /// <summary>Unplaced library variants have no outline (BaseException); the point is checked against the placed outline.</summary>
    private static double[] Outline(SymbolVariant variant)
    {
        try
        { return variant.GetBoundingBox().SelectMany(XY).ToArray(); }
        catch (BaseException)
        { return [0, 0, 0, 0]; }
    }
    private static List<double[][]> Graphical(string project, string page) => Page(project, page).AllPlacements.OfType<SymbolReference>().SelectMany(r => r.GraphicalConnections)
        .Select(g => new[] { new[] { g.StartSymbolRef.Location.X + g.StartConnectionPoint.Location.X, g.StartSymbolRef.Location.Y + g.StartConnectionPoint.Location.Y },
            new[] { g.EndSymbolRef.Location.X + g.EndConnectionPoint.Location.X, g.EndSymbolRef.Location.Y + g.EndConnectionPoint.Location.Y } }).ToList();
    public List<string?[]> Endpoints(string project, string page)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        var result = new List<string?[]>();
        foreach (Connection connection in finder.GetConnections(null).Where(c => EplanConnectionReader.PageOf(c)?.Name == page))
        {
            Pin? start = connection.StartPin; // borrowed, never disposed (A39)
            Pin? end = connection.EndPin;
            result.Add([start?.ParentFunction is { } a ? Key(a, start.Index) : null, end?.ParentFunction is { } b ? Key(b, end.Index) : null]);
        }
        return result;
    }
    private static void AssertCurrent(string project)
    {
        MainThread.Assert();
        if (!new EplanProjectCatalog().ListOpenProjects().Any(p => p.Path == project && p.Current))
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Select the target project in EPLAN first so failure compensation can undo its placement safely."); }
    }
    public void Undo(string project)
    {
        AssertCurrent(project);
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
#pragma warning disable RS0030 // O3/A41/S35: fixed current-project undo only, never a caller-supplied action.
        var manager = new ActionManager();
        using var context = new ActionCallingContext();
        if (!manager.FindAction("XEsUndoAction").Execute(context))
        { throw new InvalidOperationException("Placement undo failed."); }
#pragma warning restore RS0030
    }
}

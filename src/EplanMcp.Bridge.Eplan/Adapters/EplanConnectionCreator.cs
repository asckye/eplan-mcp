using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
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
        .SelectMany(l => l.Symbols).Where(s => s.Name is "CO" or "TLRO" or "TLRU" or "TOUL" or "TOUR" or "PCP").SelectMany(s => s.Variants);
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
                placed.Location = new PointD((double)item.Row["position"]![0]!, (double)item.Row["position"]![1]!);
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

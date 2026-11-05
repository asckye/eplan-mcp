using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A52/S40: borrowed objects stay inside one main-thread request; discovery precedes the write wrapper.</summary>
internal sealed partial class EplanConnectionCreator(bool readSymbolBounds = true, bool allowReadOnly = false) : IConnectionCreator, ICableDefinitionWriter, IConnectionDefinitionPointWriter, IConnectionPlacementReadback, IPartsDatabase
{
    private readonly List<(int Type, long Oid, string Kind)> _createdSymbols = [];

    public PartsDatabaseContent Read(string? language) => new EplanPartsDatabase().Read(language);
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
        .SelectMany(l => l.Symbols).Where(s => s.Name is "CO" or "TLRO" or "TLRU" or "TOUL" or "TOUR" or "PCP" || s.Type == Symbol.SymbolType.InterruptionPoint).SelectMany(s => s.Variants);
    public ConnectionCreatePlanner.Scene Read(string project, string page) => Read(project, page, []);
    public LayoutGuard.Model Measure(string project, string page) => LayoutGuard.From(new EplanConnectionCreator(false).Read(project, page));
    internal ConnectionCreatePlanner.Scene Read(string project, string page, IReadOnlyList<Placement> created)
    {
        OpenProject target = ProjectResolver.Resolve(new EplanProjectCatalog().ListOpenProjects(), project);
        if (target.ReadOnly && !allowReadOnly)
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Select a writable project."); }
        Page native = Page(target.Path, page);
        PageDrawingGeometry geometry = new EplanDrawingReader().Read(target.Path, page, created)!;
        geometry.Project = target.Path;
        var scene = new ConnectionCreatePlanner.Scene { Project = target, Page = geometry, PageType = native.PageType.ToString(), Complete = true, Graphical = Graphical(target.Path, page) };
        foreach (Placement placement in EplanNativePreconditions.Valid(native.AllPlacements.Concat(created)).GroupBy(p => p.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()))
        {
            if (!geometry.Placements.Any(p => p.Locator.Type == SnapshotCanonical.TypeOf(placement.ToStringIdentifier()) && p.Locator.Oid == placement.ObjectIdentifier))
            { scene.Complete = false; }
        }
        EplanConnectionScene.ReadPins(scene, EplanNativePreconditions.Valid(native.AllPlacements.Concat(created)));
        foreach (SymbolVariant variant in readSymbolBounds ? Variants(target.Path) : [])
        { scene.SymbolBounds.Add(FormattableString.Invariant($"{variant.SymbolName}/{variant.VariantNr}"), Outline(variant)); }
        scene.Logical = Endpoints(target.Path, page).Select(p => new ConnectionRecord { StartDevice = p[0], EndDevice = p[1] }).ToList();
        if (geometry.Placements.Any(p => p.Kind == nameof(ConnectionDefinitionPoint)))
        {
            ConnectionPointRouting.Initialize(scene);
            using var finder = new DMObjectsFinder(EplanProjects.ByPath(target.Path));
            foreach (Connection connection in finder.GetConnections(null).Where(c => EplanConnectionReader.PageOf(c)?.Name == page))
            {
                ConnectionDefinitionPoint[] points = connection.ConnectionDefPoints;
                if (points.Length == 0)
                { continue; }
                Pin? start = connection.StartPin, end = connection.EndPin;
                ConnectionPointRouting.Record(scene,
                    points.Select(point => FormattableString.Invariant($"{SnapshotCanonical.TypeOf(point.ToStringIdentifier())}/{point.ObjectIdentifier}")),
                    start?.ParentFunction is { } a ? Key(a, start.Index) : null,
                    end?.ParentFunction is { } b ? Key(b, end.Index) : null);
            }
        }
        scene.Layout = EplanLayout.Config();
        scene.ReadCables = () => ReadCables(target.Path, page, scene);
        return scene;
    }
    private static CableDefinitionPlanner.Inventory ReadCables(string project, string page, ConnectionCreatePlanner.Scene scene)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        var inventory = new CableDefinitionPlanner.Inventory { Complete = true, Names = EplanNativePreconditions.Valid(finder.GetFunctions(null)).ToArray().Select(f => f.Name).Where(n => !string.IsNullOrEmpty(n)).ToList() };
        foreach (Connection connection in EplanNativePreconditions.Valid(finder.GetConnections(null)).ToArray().Where(c => EplanConnectionReader.PageOf(c)?.Name == page))
        {
            Pin? start = connection.StartPin, end = connection.EndPin;
            if (start?.ParentFunction is not { } a || end?.ParentFunction is not { } b || a.Page?.Name != page || b.Page?.Name != page
                || !scene.Pins.TryGetValue(Key(a, start.Index), out SymbolConnectionPoint? from) || !scene.Pins.TryGetValue(Key(b, end.Index), out SymbolConnectionPoint? to))
            { continue; }
            // Acceptance trial: logical connections run device to device through corner and T-node symbols, so one may span
            // several drawn segments. Only a unique path through node pins makes it a core candidate; the planner still
            // refuses a definition line that crosses any drawn segment outside the selected cores.
            if (CableDefinitionPlanner.DrawnPath(scene, from.Location, to.Location) is not { } path)
            { continue; }
            CableDefinitionPlanner.Wire wire = ReadCableWire(project, page, connection, [from.Location, to.Location]);
            if (path.Length > 2)
            { wire.Path = path; }
            inventory.Wires.Add(wire);
        }
        return inventory;
    }
    // Listing and planning hash the same detached evidence, before assigning the locator itself.
    internal static CableDefinitionPlanner.Wire ReadCableWire(string project, string page, Connection connection, double[][]? points = null)
    {
        double[] Point(Pin? pin) => pin?.ParentFunction is { } owner && owner.GraphicalConnectionPoints.FirstOrDefault(p => p.Index == pin.Index) is { } graphical
            ? [owner.Location.X + graphical.Location.X, owner.Location.Y + graphical.Location.Y] : [];
        Pin? start = connection.StartPin, end = connection.EndPin;
        using ConnectionPropertyList properties = connection.Properties;
        string? Read(int number)
        { using AnyPropertyId id = number; using PropertyValue value = properties[id]; return EplanText.Read(value, EplanText.Language(null)); }
        var wire = new CableDefinitionPlanner.Wire
        {
            Points = points ?? [Point(start), Point(end)],
            Pins = [start?.ParentFunction is { } a ? Key(a, start.Index) : "", end?.ParentFunction is { } b ? Key(b, end.Index) : ""],
            Cable = connection.ConnectionDefPoints.Length > 0 ? "<definition-points>" : Read(20005),
            Label = Read(31011),
            Colour = Read(31004),
            CrossSection = Read(31002)
        };
        CableDefinitionPlanner.Identify(wire, project, page, connection.ToStringIdentifier());
        return wire;
    }
    public System.Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null)
    {
        AssertCurrent(project);
        Page target = Page(project, page);
        SymbolVariant[] variants = Variants(project).ToArray();
        var selected = symbols.Select(s => (Row: s, Variant: variants.Single(v => v.SymbolName == (string)s["symbol"]!["name"]! && v.VariantNr == (int)s["symbol"]!["variant"]!))).ToArray();
        _createdSymbols.Clear();
        return () =>
        {
            var created = new List<Placement>();
            foreach (var item in selected)
            {
                SymbolReference placed = SymbolReference.Create(item.Variant, target);
                created.Add(placed);
                _createdSymbols.Add((SnapshotCanonical.TypeOf(placed.ToStringIdentifier()), placed.ObjectIdentifier, placed.GetType().Name));
                if (item.Row["interruption"] is { } interruption)
                {
                    if (placed is not InterruptionPoint point)
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The interruption symbol did not create an interruption point."); }
                    NameInterruption(target, point, (string)interruption["name"]!);
                    if (interruption["sort_code"] is { } sortCode)
                    {
                        using InterruptionPointPropertyList properties = point.Properties;
                        properties[(Properties.InterruptionPoint)24809] = (long)sortCode;
                    }
                    if (point.Name != (string)interruption["name"]!)
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Interruption business name readback differs; supply the exact full name."); }
                }
                // Owner 2026-11-02: the symbol's default arrangement shows the cross-reference, as after EPLAN's insert
                // command; an extra 24300 text switched the point to a user-defined arrangement.
                placed.Location = new PointD((double)item.Row["position"]![0]!, (double)item.Row["position"]![1]!);
                VerifySymbol(placed, item.Row);
            }
            verifyLayout?.Invoke(LayoutGuard.From(new EplanConnectionCreator(false).Read(project, page, created)));
        };
    }
    private static void VerifySymbol(SymbolReference placed, JToken row)
    {
        double[][] pins = placed.GraphicalConnectionPoints.Select(p => XY(p.Location)).ToArray();
        SymbolConnectionPoint[] expected = row["symbol"]!["connectionPoints"]!.ToObject<SymbolConnectionPoint[]>()!;
        if (!XY(placed.Location).SequenceEqual(row["position"]!.Values<double>()) || placed.SymbolVariant.SymbolName != (string)row["symbol"]!["name"]!
            || placed.SymbolVariant.VariantNr != (int)row["symbol"]!["variant"]! || pins.Length != expected.Length
            || pins.Where((p, i) => !p.SequenceEqual(expected[i].Location)).Any()
            || placed.GraphicalConnectionPoints.Where((p, i) => p.Direction.ToString() != expected[i].Direction).Any()
            || (bool?)row["boundsKnown"] != false && !OutlineMatches(placed.GetBoundingBox().SelectMany(XY).ToArray(), row["bounds"]!.Values<double>().ToArray(), XY(placed.Location)))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, FormattableString.Invariant(
                $"Placed symbol geometry differs from the plan: {row["symbol"]!["name"]}/{row["symbol"]!["variant"]} outline {string.Join(",", placed.GetBoundingBox().SelectMany(XY))}, pins {string.Join(";", pins.Select(p => string.Join(",", p)))}."));
        }
    }

    public void VerifyCreatedSymbols(string project, string page, JArray symbols)
    {
        Placement[] placements = EplanNativePreconditions.Valid(Page(project, page).AllPlacements).ToArray();
        if (_createdSymbols.Count != symbols.Count)
        { throw SymbolMismatch("created symbol identity count changed"); }
        for (int i = 0; i < symbols.Count; i++)
        {
            var id = _createdSymbols[i];
            Placement? placement = placements.SingleOrDefault(p => p.ObjectIdentifier == id.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == id.Type);
            if (placement is not SymbolReference symbol || placement.GetType().Name != id.Kind)
            { throw SymbolMismatch("created symbol is missing or has a different native type"); }
            VerifySymbol(symbol, symbols[i]);
            if (symbol.SymbolVariant.SymbolLibraryName != (string)symbols[i]["symbol"]!["library"]!)
            { throw SymbolMismatch("created symbol library changed"); }
            if (symbols[i]["interruption"] is { } expected)
            {
                if (symbol is not InterruptionPoint point || point.Name != (string)expected["name"]!)
                { throw SymbolMismatch("created interruption name changed"); }
                if (expected["sort_code"] is { } sort)
                {
                    using InterruptionPointPropertyList properties = point.Properties;
                    using PropertyValue actual = properties[(Properties.InterruptionPoint)24809];
                    if (actual.IsEmpty || actual.ToString() != sort.ToString())
                    { throw SymbolMismatch("created interruption sort code changed"); }
                }
            }
        }
    }

    private static BridgeErrorException SymbolMismatch(string detail) => new(ErrorReason.EplanOperationFailed, "Created symbol readback differs: " + detail + ".");

    private sealed class PlacementFactory(string project, bool undoCommitted) : ITransactionScopeFactory
    { public ITransactionScope Begin(string undoDescription) => new FunctionTransactionScope(EplanProjects.ByPath(project), undoDescription, undoCommitted); }
    public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false) => new PlacementFactory(project, undoCommitted);

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
            EplanConnectionScene.ReadPins(scene, EplanNativePreconditions.Valid(native.AllPlacements).OfType<Function>());
            // A64: a T-node variant's offset pin is 2 mm from its other pins; the planner joins each node's pin positions.
            var nodes = new JArray(EplanNativePreconditions.Valid(native.AllPlacements).OfType<SymbolReference>()
                .Where(r => r is not Function && r is not DynamicConnectionLine && r is not InterruptionPoint)
                .Select(r => r.GraphicalConnectionPoints.Select(p => new[] { r.Location.X + p.Location.X, r.Location.Y + p.Location.Y }).ToArray())
                .Where(locations => locations.Length > 1).Select(locations => JArray.FromObject(locations)));
            snapshots.Add(new JObject { ["page"] = page, ["drawing"] = BridgeJson.FromObject(scene.Page), ["pins"] = BridgeJson.FromObject(scene.Pins), ["nodes"] = nodes, ["graphical"] = JArray.FromObject(scene.Graphical), ["logical"] = BridgeJson.FromObject(scene.Logical) });
            foreach (InterruptionPoint point in EplanNativePreconditions.Valid(native.AllPlacements).OfType<InterruptionPoint>())
            {
                // A55: acquire once, release before returning; no wrapper survives into Abort or compensation.
                using InterruptionPointPropertyList properties = point.Properties;
                using PropertyValue reference = properties[(Properties.InterruptionPoint)24300];
                using PropertyValue sort = properties[(Properties.InterruptionPoint)24809];
                PinBase pin = ReadInterruptionPin(point);
                points.Add(new JObject
                {
                    ["page"] = page,
                    ["name"] = point.Name,
                    ["oid"] = point.ObjectIdentifier,
                    ["position"] = new JArray(XY(point.Location)),
                    ["pin"] = new JArray(point.Location.X + pin.Location.X, point.Location.Y + pin.Location.Y),
                    ["direction"] = pin.Direction.ToString(),
                    ["symbol"] = new JObject { ["library"] = point.SymbolVariant.SymbolLibraryName, ["name"] = point.SymbolVariant.SymbolName, ["variant"] = point.SymbolVariant.VariantNr },
                    ["pins"] = new JArray(point.GraphicalConnectionPoints.Select(p => new JObject
                    { ["position"] = new JArray(point.Location.X + p.Location.X, point.Location.Y + p.Location.Y), ["direction"] = p.Direction.ToString() })),
                    ["crossReference"] = reference.IsEmpty ? null : reference.ToString(),
                    ["sortCode"] = sort.IsEmpty ? null : sort.ToString(),
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
    internal static double[] Outline(SymbolVariant variant)
    {
        try
        { return variant.GetBoundingBox().SelectMany(XY).ToArray(); }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        {
            WriteExceptionDiagnostics.Report(ex);
            return [];
        }
    }
    private static List<double[][]> Graphical(string project, string page) => EplanNativePreconditions.Valid(Page(project, page).AllPlacements).OfType<SymbolReference>().SelectMany(r => r.GraphicalConnections)
        .Select(g => new[] { new[] { g.StartSymbolRef.Location.X + g.StartConnectionPoint.Location.X, g.StartSymbolRef.Location.Y + g.StartConnectionPoint.Location.Y },
            new[] { g.EndSymbolRef.Location.X + g.EndConnectionPoint.Location.X, g.EndSymbolRef.Location.Y + g.EndConnectionPoint.Location.Y } }).ToList();
    public List<string?[]> Endpoints(string project, string page)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        var result = new List<string?[]>();
        foreach (Connection connection in EplanNativePreconditions.Valid(finder.GetConnections(null)).ToArray().Where(c => EplanConnectionReader.PageOf(c)?.Name == page))
        {
            Pin? start = connection.StartPin; // borrowed, never disposed (A39)
            Pin? end = connection.EndPin;
            string? Endpoint(Pin? pin, SymbolReference? reference) => pin?.ParentFunction is { } owner ? Key(owner, pin.Index)
                : reference is InterruptionPoint point ? InterruptionPointPlanning.Endpoint(point.Page.Name, point.Name) : null;
            result.Add([Endpoint(start, connection.StartSymbolReference), Endpoint(end, connection.EndSymbolReference)]);
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

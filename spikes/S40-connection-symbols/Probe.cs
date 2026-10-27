using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;
using Direction = Eplan.EplApi.DataModel.PinBase.Directions;

namespace EplanMcp.Spikes.S40;

// A39: pages, symbols, variants and returned placements are borrowed. Only auxiliary wrappers are disposed.
#pragma warning disable CA2000 // Persistent/borrowed StorableObjects belong to EPLAN, including factory results.
internal sealed class Probe(Project project) : IDisposable
{
    private readonly Dictionary<string, UniversalPropertyList> _lists = new(StringComparer.Ordinal);
    private readonly List<Choice> _choices = [];
    private static readonly string[] _cases = ["A", "B", "C", "D", "E"];
    internal string Stage { get; private set; } = "preflight";
    private sealed class Choice(SymbolVariant variant, Symbol.SymbolType type, JObject info, (PointD Location, Direction Direction)[] pins)
    {
        internal readonly SymbolVariant Variant = variant;
        internal readonly Symbol.SymbolType Type = type;
        internal readonly JObject Info = info;
        internal readonly (PointD Location, Direction Direction)[] Pins = pins;
    }
    private UniversalPropertyList Properties(StorableObject obj)
    {
        string key = obj.ToStringIdentifier(); // Same object/list for before, write, rollback and after (A36/A51).
        if (!_lists.TryGetValue(key, out UniversalPropertyList? list))
        {
            list = obj switch { Page p => p.Properties, Symbol s => s.Properties, _ => obj.Properties };
            _lists.Add(key, list);
        }
        return list;
    }
    private int Counter(Page page)
    {
        using PropertyValue value = ((PagePropertyList)Properties(page)).PAGE_COUNTER;
        return !value.IsEmpty && int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : -1;
    }
    private string Description(Page page)
    {
        using PropertyValue value = ((PagePropertyList)Properties(page)).PAGE_NOMINATIOMN;
        if (value.IsEmpty) { return string.Empty; }
        using MultiLangString text = value.ToMultiLangString();
        return text.GetStringToDisplay(ISOCode.Language.L_en_US);
    }
    private Page[] Pages() => project.Pages.OrderBy(p => p.ObjectIdentifier).ToArray();
    private Page[] OwnedPages() => Pages().Where(p => Counter(p) >= 9400 && Description(p).StartsWith("S40 ", StringComparison.Ordinal)).ToArray();
    private Page NewPage(int counter, string description)
    {
        Page template = Pages().First(p => p.PageType == DocumentTypeManager.DocumentType.Circuit);
        var source = (PagePropertyList)Properties(template);
        using var name = new PagePropertyList();
        foreach (Eplan.EplApi.DataModel.Properties.Page part in new[]
        {
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_FUNCTIONALASSIGNMENT,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLANT,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLACEOFINSTALLATION,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_LOCATION,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_DOCTYPE,
        })
        {
            using PropertyValue value = source[part];
            if (!value.IsEmpty) { name[part] = value; }
        }
        name.PAGE_COUNTER = Evidence.Number(counter);
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, name);
        using var title = new MultiLangString();
        title.AddString(ISOCode.Language.L_en_US, description);
        ((PagePropertyList)Properties(page)).PAGE_NOMINATIOMN = title;
        if (Counter(page) != counter || Description(page) != description) { throw new InvalidOperationException("New page identity readback differs."); }
        return page;
    }
    internal void Sentinel(Action<string, object> log)
    {
        if (OwnedPages().Length != 0) { throw new InvalidOperationException("S40 pages already exist; undo or reset the closed fixture first."); }
        var used = new HashSet<int>(Pages().Select(Counter));
        int counter = Enumerable.Range(9400, 100).First(c => !used.Contains(c) && !used.Contains(c + 1));
        Page page = NewPage(counter, "S40 sentinel");
        log("sentinel", new { counter, name = page.Name, oid = page.ObjectIdentifier, grid = page.GridSize });
    }
    private static bool Routing(Symbol.SymbolType type) => type.ToString().StartsWith("Routing", StringComparison.Ordinal) || type.ToString().StartsWith("TNode", StringComparison.Ordinal) ||
        type is Symbol.SymbolType.DynamicRouting or Symbol.SymbolType.StrandConnectorAngle or Symbol.SymbolType.PotentialTerminal or Symbol.SymbolType.PotentialDefinition;
    internal void Discover(string pageName, Action<string, object> log)
    {
        using var quiet = new Eplan.EplApi.ApplicationFramework.QuietModeStep(Eplan.EplApi.ApplicationFramework.QuietModes.ShowNoDialogs);
        var watch = Stopwatch.StartNew();
        _choices.Clear();
        foreach (SymbolLibrary library in project.SymbolLibraries.OrderBy(l => l.Name, StringComparer.Ordinal))
        {
            Symbol[] symbols = library.Symbols;
            log("library", new { name = library.Name, symbols = symbols.Length, ownership = "Project.SymbolLibraries; no Initialize(system), Close or Dispose" });
            foreach (Symbol symbol in symbols.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) { throw new TimeoutException("Symbol discovery exceeded 90 s; inventory is incomplete."); }
                Symbol.SymbolType type = symbol.Type;
                if (!Routing(type) && type != Symbol.SymbolType.Function) { continue; }
                var variants = new JArray();
                foreach (SymbolVariant variant in symbol.Variants.OrderBy(v => v.VariantNr))
                {
                    // A39: pins are StorableObjects owned by EPLAN; never Dispose them (the product adapters do not either).
                    (PointD Location, Direction Direction)[] pins = variant.ConnectionPoints.Select(p => (p.Location, p.Direction)).ToArray();
                    if (!Routing(type) && pins.Length != 2) { continue; }
                    var info = new JObject { ["library"] = library.Name, ["symbol"] = symbol.Name, ["type"] = type.ToString(), ["variant"] = variant.VariantNr,
                        ["pins"] = new JArray(pins.Select((p, i) => new JObject { ["arrayIndex"] = i, ["location"] = XY(p.Location), ["direction"] = p.Direction.ToString() })) };
                    variants.Add(info.DeepClone());
                    _choices.Add(new Choice(variant, type, info, pins));
                }
                if (variants.Count == 0 && !Routing(type)) { continue; }
                object? number;
                try { using PropertyValue value = ((SymbolPropertyList)Properties(symbol)).SYMB_NR; number = value.IsEmpty ? null : value.ToString(); }
                catch (PropertyNotFoundException ex) { number = Evidence.Error(ex); } // A36: unavailable differs from an empty value.
                log(Routing(type) ? "routing_symbol" : "two_pole_candidate", new { library = library.Name, symbol = symbol.Name, type = type.ToString(), number, variants });
            }
        }
        if (pageName.Length > 0)
        {
            Page page = Pages().Single(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
            log("supplied_page_grid", new { name = page.Name, grid = page.GridSize, placements = page.AllPlacements.Length, fresh = page.AllPlacements.Length == 0 });
        }
    }
    internal void GenerateOnly(Action<string, object> log)
    {
        Stage = "Generate.Connections";
        Page page = OwnedPages().Single(p => Description(p) == "S40 place");
        log("before_generate", ReadPage(page, []));
        using (var generator = new Generate()) { generator.Connections([page], false); }
        Stage = "readback";
        log("after_generate", ReadPage(page, []));
    }
    internal void Place(bool generate, Action<string, object> log)
    {
        Stage = "preflight:owned";
        Page sentinel = OwnedPages().Single(p => Description(p) == "S40 sentinel");
        int counter = Counter(sentinel) + 1;
        if (Pages().Any(p => Counter(p) == counter)) { throw new InvalidOperationException("The reserved place counter exists; do not place twice."); }
        Stage = "preflight:finder";
        using var finder = new DMObjectsFinder(project);
        if (finder.GetFunctions(null).Any(f => f.Name.IndexOf("-S40", StringComparison.OrdinalIgnoreCase) >= 0)) { throw new InvalidOperationException("Reserved S40 function tags already exist; refusing to join an existing device."); }
        if (_choices.Count == 0) { Discover(string.Empty, log); }
        Stage = "preflight:new_page";
        Page page = NewPage(counter, "S40 place");
        // r3: LockObject on the page created in this SafetyPoint fails (S063176); the product never locks objects explicitly.
        Stage = "preflight:pick";
        double grid = page.GridSize;
        if (double.IsNaN(grid) || double.IsInfinity(grid) || grid <= 0) { throw new InvalidOperationException("Invalid fresh-page grid."); }
        bool Usable(Choice c) => c.Pins.All(p => p.Direction is Direction.Up or Direction.Down or Direction.Left or Direction.Right && Math.Abs(p.Location.X) <= 2 * grid && Math.Abs(p.Location.Y) <= 2 * grid &&
            Math.Abs(p.Location.X / grid - Math.Round(p.Location.X / grid)) < 1e-6 && Math.Abs(p.Location.Y / grid - Math.Round(p.Location.Y / grid)) < 1e-6);
        Choice Pick(Func<Choice, bool> predicate, string role) => _choices.FirstOrDefault(c => Usable(c) && predicate(c)) ?? throw new InvalidOperationException("No discovered on-grid candidate for " + role + "; inspect discover; no substitute or import.");
        bool Has(Choice c, Direction d) => c.Pins.Any(p => p.Direction == d);
        Choice vertical = Pick(c => c.Type == Symbol.SymbolType.Function && c.Pins.Length == 2 && Has(c, Direction.Up) && Has(c, Direction.Down) && c.Pins[0].Location.X == c.Pins[1].Location.X, "vertical two-pole");
        Choice horizontal = Pick(c => c.Type == Symbol.SymbolType.Function && c.Pins.Length == 2 && Has(c, Direction.Left) && Has(c, Direction.Right) && c.Pins[0].Location.Y == c.Pins[1].Location.Y, "horizontal two-pole");
        Choice corner = Pick(c => c.Type is Symbol.SymbolType.Routing or Symbol.SymbolType.StrandConnectorAngle && c.Pins.Length == 2 &&
            c.Pins.Any(p => p.Direction is Direction.Up or Direction.Down) && c.Pins.Any(p => p.Direction is Direction.Left or Direction.Right), "corner");
        Choice tee = Pick(c => c.Type.ToString().StartsWith("TNode", StringComparison.Ordinal) && c.Pins.Length == 3 && c.Pins.Select(p => p.Direction).Distinct().Count() == 3 && c.Pins.All(p => p.Direction != Direction.Undefined), "T-node");
        Choice potential = Pick(c => c.Type == Symbol.SymbolType.PotentialTerminal && c.Pins.Length == 1 && c.Pins[0].Direction != Direction.Undefined, "PotentialTerminal");
        var placed = new List<SymbolReference>();
        log("fresh_page", new { page = page.Name, counter, grid, coordinateUnit = "API units; mm and visual origin await real run", boundaryVerified = false });
        SymbolReference Put(Choice choice, PointD location, string test, bool function)
        {
            Stage = function ? "Function.Create" : "SymbolReference.Create";
            SymbolReference reference;
            if (function) { var f = new Function(); f.Create(page, choice.Variant); reference = f; }
            else { reference = SymbolReference.Create(choice.Variant, page); } // Factory preserves native specialization; log the actual runtime type.
            Stage = "position/readback";
            reference.Location = location;
            placed.Add(reference);
            if (reference is Function created) { created.Name = "-S40" + test + Evidence.Number(placed.Count); }
            PointD actual = reference.Location;
            log("placed", new { test, selected = choice.Info, oid = reference.ObjectIdentifier, runtimeType = reference.GetType().FullName, requested = XY(location), actual = XY(actual) });
            if (Math.Abs(location.X - actual.X) > 1e-6 || Math.Abs(location.Y - actual.Y) > 1e-6 || reference is Function part && part.ArticleReferences.Length != 0) { throw new InvalidOperationException("Placement position/part readback differs."); }
            return reference;
        }
        void Endpoint(PointD target, Direction direction, string test)
        {
            Choice choice = direction is Direction.Up or Direction.Down ? vertical : horizontal;
            PointD offset = choice.Pins.Single(p => p.Direction == direction).Location;
            log("requested_pin", new { test, target = XY(target), direction = direction.ToString(), libraryOffset = XY(offset) });
            Put(choice, new PointD(target.X - offset.X, target.Y - offset.Y), test, true);
        }
        Endpoint(new PointD(10 * grid, 45 * grid), Direction.Down, "A");
        Endpoint(new PointD(10 * grid, 35 * grid), Direction.Up, "A");
        Endpoint(new PointD(30 * grid, 45 * grid), Direction.Down, "B");
        Endpoint(new PointD(31 * grid, 35 * grid), Direction.Up, "B"); // Negative control: exactly one grid of horizontal misalignment.
        foreach ((Choice choice, int x, int y, string test) in new[] { (corner, 10, 15, "C"), (tee, 35, 15, "D"), (potential, 60, 40, "E") })
        {
            PointD origin = new(x * grid, y * grid);
            Put(choice, origin, test, false);
            foreach ((PointD offset, Direction direction) in choice.Pins)
            {
                (int dx, int dy, Direction opposite) = direction switch { Direction.Up => (0, 1, Direction.Down), Direction.Down => (0, -1, Direction.Up), Direction.Left => (-1, 0, Direction.Right), Direction.Right => (1, 0, Direction.Left), _ => throw new InvalidOperationException("Non-cardinal direction.") };
                Endpoint(new PointD(origin.X + offset.X + dx * 6 * grid, origin.Y + offset.Y + dy * 6 * grid), opposite, test);
            }
        }
        Stage = "readback";
        log("before_generate", ReadPage(page, placed)); // A13: include returned objects; finder may not see uncommitted functions.
        if (generate)
        {
            Stage = "Generate.Connections";
            log("generate_enter", new { page = page.Name, rebuildAll = false });
            using var generator = new Generate();
            generator.Connections([page], false);
            Stage = "readback";
            log("after_generate", ReadPage(page, placed));
        }
        else { log("generate_skipped", new { requested = false }); }
    }
    internal JObject Read()
    {
        using var quiet = new Eplan.EplApi.ApplicationFramework.QuietModeStep(Eplan.EplApi.ApplicationFramework.QuietModes.ShowNoDialogs);
        using var finder = new DMObjectsFinder(project);
        return new JObject { ["pageOids"] = new JArray(Pages().Select(p => p.ObjectIdentifier)), ["functionOids"] = new JArray(finder.GetFunctions(null).Select(f => f.ObjectIdentifier).OrderBy(x => x)), ["connectionOids"] = new JArray(finder.GetConnections(null).Select(c => c.ObjectIdentifier).OrderBy(x => x)),
            ["pages"] = new JArray(OwnedPages().Select(p => ReadPage(p, []))) };
    }
    private static JObject XY(PointD p) => new() { ["x"] = p.X, ["y"] = p.Y };
    private static string Case(SymbolReference reference, double grid)
    {
        PointD p = reference.Location;
        return p.Y > 28 * grid ? p.X < 20 * grid ? "A" : p.X < 50 * grid ? "B" : "E" : p.X < 22 * grid ? "C" : "D";
    }
    private static JObject? PinData(PinBase? pin, SymbolReference? owner)
    {
        if (pin is null) { return null; }
        PointD local = pin.Location;
        return new JObject { ["index"] = pin.Index, ["location"] = XY(local), ["direction"] = pin.Direction.ToString(), ["designation"] = pin is Pin p ? p.Designation : null,
            ["parentOid"] = owner?.ObjectIdentifier, ["pagePositionByAddition"] = owner is null ? null : XY(new PointD(owner.Location.X + local.X, owner.Location.Y + local.Y)) };
    }
    private JObject ReadPage(Page page, IEnumerable<SymbolReference> returned)
    {
        double grid = page.GridSize;
        var symbols = new JArray();
        var connections = new Dictionary<string, Connection>(StringComparer.Ordinal);
        using var finder = new DMObjectsFinder(project);
        Connection[] found = finder.GetConnections(null).Where(c => c.Page?.ObjectIdentifier == page.ObjectIdentifier).ToArray();
        foreach (Connection connection in found) { connections[connection.ToStringIdentifier()] = connection; }
        foreach (SymbolReference reference in page.AllPlacements.OfType<SymbolReference>().Concat(returned).GroupBy(r => r.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()).OrderBy(r => r.ObjectIdentifier))
        {
            var row = new JObject { ["oid"] = reference.ObjectIdentifier, ["runtimeType"] = reference.GetType().FullName, ["case"] = Case(reference, grid), ["location"] = XY(reference.Location),
                ["library"] = reference.SymbolVariant.SymbolLibraryName, ["symbol"] = reference.SymbolVariant.SymbolName, ["variant"] = reference.SymbolVariant.VariantNr };
            row["graphicalPins"] = new JArray(reference.GraphicalConnectionPoints.Select(p => PinData(p, reference)));
            row["graphicalConnections"] = new JArray(reference.GraphicalConnections.Select(g =>
            {
                PinBase start = g.StartConnectionPoint;
                PinBase end = g.EndConnectionPoint;
                return new JObject { ["start"] = PinData(start, g.StartSymbolRef), ["end"] = PinData(end, g.EndSymbolRef) };
            }));
            if (reference is Function function)
            {
                row["name"] = function.Name;
                row["parts"] = function.ArticleReferences.Length;
                Pin[] pins = function.Pins;
                row["pins"] = new JArray(pins.Select(p => PinData(p, function)));
                foreach (Connection c in pins.SelectMany(p => p.Connections)) { connections[c.ToStringIdentifier()] = c; }
            }
            symbols.Add(row);
        }
        var logical = new JArray(connections.Values.OrderBy(c => c.ObjectIdentifier).Select(c =>
        {
            using Pin start = c.StartPin;
            using Pin end = c.EndPin;
            return new JObject { ["oid"] = c.ObjectIdentifier, ["start"] = PinData(start, start?.ParentFunction), ["end"] = PinData(end, end?.ParentFunction),
                ["startCase"] = start?.ParentFunction is Function a ? Case(a, grid) : null, ["endCase"] = end?.ParentFunction is Function b ? Case(b, grid) : null };
        }));
        return new JObject { ["oid"] = page.ObjectIdentifier, ["name"] = page.Name, ["counter"] = Counter(page), ["description"] = Description(page), ["grid"] = grid, ["symbols"] = symbols,
            ["finderConnectionCount"] = found.Length, ["connections"] = logical, ["cases"] = new JArray(_cases.Select(test =>
                new JObject { ["case"] = test, ["logicalExists"] = logical.Any(c => (string?)c["startCase"] == test || (string?)c["endCase"] == test),
                    ["internalLogicalExists"] = logical.Any(c => (string?)c["startCase"] == test && (string?)c["endCase"] == test),
                    ["crossCaseLogicalExists"] = logical.Any(c => ((string?)c["startCase"] == test) != ((string?)c["endCase"] == test)),
                    ["logicalConnectionOids"] = new JArray(logical.Where(c => (string?)c["startCase"] == test || (string?)c["endCase"] == test).Select(c => c["oid"])),
                    ["graphicalSegmentsReported"] = symbols.Where(s => (string?)s["case"] == test).Sum(s => ((JArray)s["graphicalConnections"]!).Count) })) };
    }
    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (UniversalPropertyList list in _lists.Values) { try { list.Dispose(); } catch (Exception ex) { failures.Add(ex); } }
        _lists.Clear();
        _choices.Clear();
        if (failures.Count > 0) { throw new AggregateException("Property-list release failed.", failures); }
    }
}
#pragma warning restore CA2000

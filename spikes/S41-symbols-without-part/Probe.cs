using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S41;

// A36/A39/A52: cache each property list once; never Dispose pins, libraries or any StorableObject.
#pragma warning disable CA2000 // Persistent/borrowed objects, including factory results, belong to EPLAN.
internal sealed class Probe(Project project) : IDisposable
{
    private readonly Dictionary<string, UniversalPropertyList> _lists = new(StringComparer.Ordinal);
    private readonly List<Choice> _choices = [];
    private Choice[] _selected = [];
    // Function / FunctionBase entries in data-dictionary.jsonl; these are display experiments, not arbitrary writes.
    private static readonly int[] _tagIds = [20058, 20214, 20213, 20211, 20212, 20215, 20010];
    private static readonly string[] _roles = ["coil", "no_contact", "two_pole"];
    private static readonly string[] _tags = ["=GAA+A1-S41K1", "=GAA+A1-S41K2", "=GAA+A1-S41X1"];
    private sealed class Choice(SymbolVariant variant, FunctionCategory? category, JObject info)
    {
        internal readonly SymbolVariant Variant = variant;
        internal readonly FunctionCategory? Category = category;
        internal readonly JObject Info = info;
    }
    private UniversalPropertyList Properties(StorableObject obj)
    {
        string key = obj.ToStringIdentifier();
        if (!_lists.TryGetValue(key, out UniversalPropertyList? list))
        {
            list = obj switch { Page p => p.Properties, Function f => f.Properties, Symbol s => s.Properties, _ => obj.Properties };
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
    private Page[] OwnedPages() => Pages().Where(p => Counter(p) is >= 9500 and <= 9599 && Description(p).StartsWith("S41 ", StringComparison.Ordinal)).ToArray();
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
        if (OwnedPages().Length != 0) { throw new InvalidOperationException("S41 pages already exist; undo or reset the closed fixture first."); }
        var used = new HashSet<int>(Pages().Select(Counter));
        int counter = Enumerable.Range(9500, 99).First(c => !used.Contains(c) && !used.Contains(c + 1));
        Page page = NewPage(counter, "S41 sentinel");
        log("sentinel", new { counter, name = page.Name, oid = page.ObjectIdentifier, grid = page.GridSize });
    }
    internal void Discover(string pageName, Action<string, object> log)
    {
        using var quiet = new Eplan.EplApi.ApplicationFramework.QuietModeStep(Eplan.EplApi.ApplicationFramework.QuietModes.ShowNoDialogs);
        using var finder = new DMObjectsFinder(project);
        // Symbol exposes no FunctionCategory getter. Prefer categories of existing placed uses, then encoded 20188.
        var used = finder.GetFunctions(null).Where(f => f.IsPlaced).GroupBy(f => (f.SymbolVariant.SymbolLibraryName, f.SymbolVariant.SymbolName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.FunctionCategory).Distinct().ToArray());
        var watch = Stopwatch.StartNew();
        _choices.Clear();
        _selected = [];
        foreach (SymbolLibrary library in project.SymbolLibraries.OrderBy(l => l.Name, StringComparer.Ordinal))
        {
            log("library", new { library = library.Name, inspected = library.Name == "IEC_symbol", ownership = "Project.SymbolLibraries; no Initialize, Close or Dispose" });
            if (library.Name != "IEC_symbol") { continue; }
            foreach (Symbol symbol in library.Symbols.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) { throw new TimeoutException("Symbol discovery exceeded 90 s; inventory is incomplete."); }
                if (symbol.Type != Symbol.SymbolType.Function) { continue; }
                JToken? definition = Value(symbol, 20188, false);
                string encoded = definition?.Type == JTokenType.String ? definition.Value<string>() ?? string.Empty : string.Empty;
                FunctionCategory? category = null;
                string source = "unavailable";
                if (used.TryGetValue((library.Name, symbol.Name), out FunctionCategory[]? categories) && categories.Length == 1)
                { category = categories[0]; source = "existing Function.FunctionCategory"; }
                else if (int.TryParse(encoded.Split('/')[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && Enum.IsDefined(typeof(FunctionCategory), (FunctionCategory)number))
                { category = (FunctionCategory)number; source = "20188 category prefix; verify on created Function"; }
                JToken? symbolNumber = Value(symbol, 16002, false), categoryText = Value(symbol, 20115, true);
                foreach (SymbolVariant variant in symbol.Variants.OrderBy(v => v.VariantNr))
                {
                    PinBase[] pins = variant.ConnectionPoints;
                    if (pins.Length != 2) { continue; }
                    var info = new JObject { ["library"] = library.Name, ["symbol"] = symbol.Name, ["type"] = symbol.Type.ToString(),
                        ["number"] = symbolNumber?.DeepClone(), ["variant"] = variant.VariantNr,
                        ["category"] = category?.ToString(), ["categorySource"] = source, ["categoryGroupId"] = definition?.DeepClone(), ["categoryText"] = categoryText?.DeepClone(),
                        ["pins"] = new JArray(pins.Select(p => new JObject { ["location"] = XY(p.Location), ["direction"] = p.Direction.ToString() })) };
                    _choices.Add(new Choice(variant, category, info));
                    log("symbol_candidate", info);
                }
            }
        }
        Choice? coil = _choices.FirstOrDefault(c => c.Category == FunctionCategory.Coil)
            ?? _choices.FirstOrDefault(c => c.Category is null && (string?)c.Info["symbol"] == "K"); // A26 fallback, still verified after creation.
        Choice? contact = _choices.FirstOrDefault(c => c.Category == FunctionCategory.NoContact);
        Choice? twoPole = _choices.FirstOrDefault(c => c.Category is not (FunctionCategory.Coil or FunctionCategory.NoContact)
            && (string?)c.Info["symbol"] != (string?)coil?.Info["symbol"] && (string?)c.Info["symbol"] != (string?)contact?.Info["symbol"]
            && !(c.Category?.ToString().IndexOf("Terminal", StringComparison.Ordinal) >= 0) && !(c.Category?.ToString().StartsWith("PLC", StringComparison.Ordinal) == true));
        Choice?[] picked = [coil, contact, twoPole];
        for (int i = 0; i < picked.Length; i++) { log("selected", new { role = _roles[i], selected = picked[i]?.Info, missing = picked[i] is null }); }
        if (picked.All(c => c is not null)) { _selected = picked.Select(c => c!).ToArray(); }
        log("display_candidates", new { tagPropertyIds = _tagIds, functionText = 20011, technicalData = 20027, source = "catalog/eplan-2026.0.3/data-dictionary.jsonl" });
        if (pageName.Length > 0)
        {
            Page page = Pages().Single(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
            log("supplied_page", ReadPage(page, []));
        }
    }
    internal void Preflight()
    {
        if (_selected.Length != 3) { throw new InvalidOperationException("Missing discovered coil/NO contact/two-pole sample; inspect discover; no import or substitute."); }
        Page sentinel = OwnedPages().Single(p => Description(p) == "S41 sentinel");
        if (Pages().Any(p => Counter(p) == Counter(sentinel) + 1)) { throw new InvalidOperationException("Reserved place counter exists; do not place twice."); }
        using var finder = new DMObjectsFinder(project);
        if (finder.GetFunctions(null).Any(f => f.Name.IndexOf("-S41", StringComparison.OrdinalIgnoreCase) >= 0))
        { throw new InvalidOperationException("Reserved S41 tags exist; refusing to join an existing device."); }
    }
    internal void Place(Action<string, object> log)
    {
        if (_selected.Length != 3) { throw new InvalidOperationException("Discover must finish before entering the write scope."); }
        Page sentinel = OwnedPages().Single(p => Description(p) == "S41 sentinel");
        Page page = NewPage(Counter(sentinel) + 1, "S41 place");
        double grid = page.GridSize;
        if (double.IsNaN(grid) || double.IsInfinity(grid) || grid <= 0) { throw new InvalidOperationException("Invalid fresh-page grid."); }
        var returned = new List<Function>();
        var createdPlacements = new List<PropertyPlacement>();
        log("fresh_page", new { page = page.Name, grid, boundaryVerified = false, coordinateUnit = "API units; visible origin and text coordinates await real run" });
        for (int i = 0; i < _selected.Length; i++)
        {
            Choice choice = _selected[i];
            log("create_enter", new { role = _roles[i], selected = choice.Info, tag = _tags[i] });
            var function = new Function();
            function.Create(page, choice.Variant);
            returned.Add(function);
            PointD origin = new((15 + 30 * i) * grid, 35 * grid);
            function.Location = origin;
            log("default_after_create", ReadPage(page, returned));
            function.Name = _tags[i];
            FunctionCategory? expected = i == 0 ? FunctionCategory.Coil : i == 1 ? FunctionCategory.NoContact : choice.Category;
            if (function.Name != _tags[i] || (expected is not null && function.FunctionCategory != expected) || function.Pins.Length != 2 || function.ArticleReferences.Length != 0 || !Same(function.Location, origin))
            { throw new InvalidOperationException("Function tag/category/pin count/part/position readback differs."); }
            SetProperties(function, _roles[i], log);
            log("default_after_values", ReadPage(page, returned));
            int row = 0;
            foreach (int property in _tagIds.Concat([20011, 20027]))
            {
                using AnyPropertyId id = property;
                var placement = new PropertyPlacement();
                log("property_create_enter", new { role = _roles[i], property, parentOid = function.ObjectIdentifier });
                placement.Create(function, id);
                createdPlacements.Add(placement);
                PointD offset = new(4 * grid, (6 - 2 * row++) * grid);
                placement.Justification = TextBase.JustificationType.BaseLineLeft;
                placement.Height = 2.5;
                placement.IsVisible = true;
                // Keep both requests: the live run must establish whether Location is relative or absolute.
                placement.Location = offset;
                log("offset_only", new { role = _roles[i], property, requestedOffset = XY(offset), state = PlacementData(placement) });
                placement.Location = new PointD(origin.X + offset.X, origin.Y + offset.Y);
                log("parent_plus_offset", new { role = _roles[i], property, requestedOffset = XY(offset), requested = XY(new PointD(origin.X + offset.X, origin.Y + offset.Y)), state = PlacementData(placement) });
            }
        }
        // A13: finder alone cannot see uncommitted functions. Read returned objects plus page/container enumeration.
        log("inside_transaction", ReadPage(page, returned, createdPlacements));
        if (returned.Where((f, i) => f.Name != _tags[i] || f.ArticleReferences.Length != 0).Any())
        { throw new InvalidOperationException("Final tag/part readback differs; refusing commit."); }
        log("display_summary", new { functions = returned.Select(f => new { tag = f.Name, parts = f.ArticleReferences.Length,
            candidates = f.PropertyPlacements.Where(p => { using AnyPropertyId id = p.DisplayedProperty; return _tagIds.Contains(id.AsInt); })
                .Select(p => new { state = PlacementData(p), startsWithHyphen = p.GetDisplayString().StartsWith("-", StringComparison.Ordinal), containsRequestedTagWithPrefix = p.GetDisplayString().IndexOf(f.Name.Substring(f.Name.LastIndexOf('-')), StringComparison.Ordinal) >= 0 }).ToArray() }).ToArray(),
            visualVerified = false });
    }
    private void SetProperties(Function function, string role, Action<string, object> log)
    {
        var props = (FunctionPropertyList)Properties(function);
        using (PropertyValue initial = props.FUNC_TEXT)
        using (MultiLangString seed = initial.IsEmpty ? new MultiLangString() : initial.ToMultiLangString())
        {
            using var german = new ISOCode(ISOCode.Language.L_de_DE);
            log("function_text_initial", new { tag = function.Name, languages = Languages(seed) });
            if (Languages(seed).Property(german.GetString(), StringComparison.Ordinal) is null) { seed.AddString(ISOCode.Language.L_de_DE, "S41 preserved German"); }
            props.FUNC_TEXT = seed;
        }
        using PropertyValue before = props.FUNC_TEXT;
        using MultiLangString merged = before.ToMultiLangString();
        JObject original = Languages(merged);
        string requested = "S41 " + role + " function text";
        merged.AddString(ISOCode.Language.L_en_US, requested);
        props.FUNC_TEXT = merged; // Merge into existing content; never replace other languages with a plain string.
        string technical = role == "coil" ? "DC 24 V" : role == "no_contact" ? "NO 13-14" : "S41 two-pole data";
        props.FUNC_TECHNICAL_CHARACTERISTIC = technical;
        using PropertyValue after = props.FUNC_TEXT;
        using MultiLangString read = after.ToMultiLangString();
        JObject actual = Languages(read);
        using var english = new ISOCode(ISOCode.Language.L_en_US);
        bool preserved = original.Properties().Where(p => p.Name != english.GetString()).All(p => JToken.DeepEquals(p.Value, actual[p.Name]));
        using PropertyValue data = props.FUNC_TECHNICAL_CHARACTERISTIC;
        log("property_merge", new { tag = function.Name, before = original, after = actual, otherLanguagesPreserved = preserved, technical });
        if (!preserved || read.GetString(ISOCode.Language.L_en_US) != requested || data.IsEmpty || data.ToString() != technical)
        { throw new InvalidOperationException("Function text language preservation or technical data readback differs."); }
    }
    internal JObject Read()
    {
        using var quiet = new Eplan.EplApi.ApplicationFramework.QuietModeStep(Eplan.EplApi.ApplicationFramework.QuietModes.ShowNoDialogs);
        using var finder = new DMObjectsFinder(project);
        return new JObject { ["pageOids"] = new JArray(Pages().Select(p => p.ObjectIdentifier)),
            ["functionOids"] = new JArray(finder.GetFunctions(null).Select(f => f.ObjectIdentifier).OrderBy(x => x)),
            ["partOids"] = new JArray(finder.GetArticleReferences(null).Select(r => r.ObjectIdentifier).OrderBy(x => x)),
            ["connectionOids"] = new JArray(finder.GetConnections(null).Select(c => c.ObjectIdentifier).OrderBy(x => x)),
            ["pages"] = new JArray(OwnedPages().Select(p => ReadPage(p, []))) };
    }
    private JObject ReadPage(Page page, IEnumerable<Function> returned, IEnumerable<PropertyPlacement>? created = null)
    {
        Placement[] all = page.AllPlacements;
        Function[] functions = all.OfType<Function>().Concat(returned).GroupBy(f => f.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => f.ObjectIdentifier).ToArray();
        var placements = PropertyPlacements(all.Concat(returned).Concat(created ?? [])).OrderBy(p => p.ObjectIdentifier);
        return new JObject { ["oid"] = page.ObjectIdentifier, ["name"] = page.Name, ["counter"] = Counter(page), ["description"] = Description(page), ["grid"] = page.GridSize,
            ["pagePlacementOids"] = new JArray(all.Select(p => p.ObjectIdentifier).OrderBy(x => x)),
            ["functions"] = new JArray(functions.Select(f => new JObject { ["oid"] = f.ObjectIdentifier, ["name"] = f.Name, ["visibleName"] = f.VisibleName,
                ["category"] = f.FunctionCategory.ToString(), ["parts"] = f.ArticleReferences.Length, ["location"] = XY(f.Location),
                ["library"] = f.SymbolVariant.SymbolLibraryName, ["symbol"] = f.SymbolVariant.SymbolName, ["variant"] = f.SymbolVariant.VariantNr,
                ["useLocalPropertyPlacements"] = f.UseLocalPropertyPlacements, ["functionText"] = Value(f, 20011, true), ["technicalData"] = Value(f, 20027, false),
                ["pins"] = new JArray(f.Pins.Select(p => new JObject { ["index"] = p.Index, ["designation"] = p.Designation, ["location"] = XY(p.Location), ["direction"] = p.Direction.ToString() })) })),
            ["propertyPlacements"] = new JArray(placements.Select(PlacementData)) };
    }
    private static IEnumerable<PropertyPlacement> PropertyPlacements(IEnumerable<Placement> roots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Placement>(roots);
        while (pending.Count > 0)
        {
            Placement placement = pending.Pop();
            if (!seen.Add(placement.ToStringIdentifier())) { continue; }
            if (placement is PropertyPlacement property) { yield return property; }
            if (placement is Group group) { foreach (Placement child in group.SubPlacements) { pending.Push(child); } }
            IEnumerable<PropertyPlacement> properties = placement is IPropertyPlacementsContainer container ? container.PropertyPlacements : placement is SymbolReference reference ? reference.PropertyPlacements : [];
            foreach (PropertyPlacement child in properties) { pending.Push(child); }
        }
    }
    private JToken? Value(StorableObject obj, int property, bool multilingual)
    {
        using AnyPropertyId id = property;
        try
        {
            using PropertyValue value = Properties(obj)[id];
            if (value.IsEmpty) { return null; }
            if (!multilingual) { return new JValue(value.ToString()); }
            using MultiLangString text = value.ToMultiLangString();
            return Languages(text);
        }
        catch (PropertyNotFoundException ex) { return Evidence.Error(ex); } // Missing metadata is a discovery gap (A36), not a guessed category.
    }
    private static JObject PlacementData(PropertyPlacement placement)
    {
        using AnyPropertyId id = placement.DisplayedProperty;
        Placement? parent = placement.Parent as Placement;
        PointD location = placement.Location;
        return new JObject { ["oid"] = placement.ObjectIdentifier, ["propertyId"] = id.AsInt, ["index"] = id.Index,
            ["display"] = placement.GetDisplayString(), ["location"] = XY(location), ["justification"] = placement.Justification.ToString(), ["height"] = placement.Height, ["isVisible"] = placement.IsVisible,
            ["parentOid"] = parent?.ObjectIdentifier, ["parentLocation"] = parent is null ? null : XY(parent.Location),
            ["locationMinusParent"] = parent is null ? null : XY(new PointD(location.X - parent.Location.X, location.Y - parent.Location.Y)),
            ["boundsAbsolute"] = Bounds(placement, false), ["boundsRelative"] = Bounds(placement, true) };
    }
    private static JToken Bounds(PropertyPlacement placement, bool relative)
    {
        try { return new JArray(placement.GetBoundingBox(relative).Select(XY)); }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        { return Evidence.Error(ex); } // Empty default texts may have no box; preserve this diagnostic rather than inventing coordinates.
    }
    private static JObject Languages(MultiLangString text)
    {
        var languages = new LanguageList();
        int count = text.GetLanguageList(ref languages);
        var result = new JObject();
        for (int i = 0; i < count; i++)
        {
            ISOCode.Language language = languages.get_Language(i);
            using var code = new ISOCode(language);
            result[code.GetString()] = text.GetString(language);
        }
        return new JObject(result.Properties().OrderBy(p => p.Name, StringComparer.Ordinal));
    }
    private static JObject XY(PointD p) => new() { ["x"] = p.X, ["y"] = p.Y };
    private static bool Same(PointD a, PointD b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (UniversalPropertyList list in _lists.Values) { try { list.Dispose(); } catch (Exception ex) { failures.Add(ex); } }
        _lists.Clear();
        _choices.Clear();
        _selected = [];
        if (failures.Count > 0) { throw new AggregateException("Property-list release failed.", failures); }
    }
}
#pragma warning restore CA2000

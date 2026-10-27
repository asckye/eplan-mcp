using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S42;

// A36/A39/A52: cache property lists; EPLAN owns all persistent objects and borrowed pins/libraries.
#pragma warning disable CA2000 // Never Dispose StorableObjects, including factory results.
internal sealed partial class Probe(Project project) : IDisposable
{
    private readonly Dictionary<string, UniversalPropertyList> _lists = new(StringComparer.Ordinal);
    private SymbolVariant? _coil;
    private const string Tag = "=GAA+A1-S42K1";
    internal string Stage { get; private set; } = "preflight";
    internal bool NeedsFunction { get; private set; }
    private UniversalPropertyList Properties(StorableObject obj)
    {
        string key = obj.ToStringIdentifier();
        if (!_lists.TryGetValue(key, out UniversalPropertyList? list))
        {
            list = obj switch { Page p => p.Properties, Function f => f.Properties, _ => obj.Properties };
            _lists.Add(key, list);
        }
        return list;
    }
    private Page[] Pages() => project.Pages.OrderBy(p => p.ObjectIdentifier).ToArray();
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
    private Page[] OwnedPages() => Pages().Where(p => Counter(p) is >= 9600 and <= 9699 && Description(p).StartsWith("S42 ", StringComparison.Ordinal)).ToArray();
    private Page Drawing() => OwnedPages().Single(p => Description(p) == "S42 graphics");
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
        Stage = "Page.Create";
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, name);
        using var title = new MultiLangString();
        title.AddString(ISOCode.Language.L_en_US, description);
        ((PagePropertyList)Properties(page)).PAGE_NOMINATIOMN = title;
        if (Counter(page) != counter || Description(page) != description) { throw new InvalidOperationException("New page identity readback differs."); }
        return page;
    }
    internal void Preflight(string step, Action<string, object> log)
    {
        Stage = "preflight";
        if (step == "sentinel")
        {
            if (OwnedPages().Length != 0) { throw new InvalidOperationException("S42 pages exist; undo or reset the closed fixture first."); }
            return;
        }
        Page sentinel = OwnedPages().Single(p => Description(p) == "S42 sentinel");
        if (step == "create")
        {
            if (Pages().Any(p => Counter(p) == Counter(sentinel) + 1)) { throw new InvalidOperationException("Reserved drawing counter exists; do not create twice."); }
            return;
        }
        Page page = Drawing();
        Placement[] all = Walk(page.AllPlacements).ToArray();
        if (all.Count(p => p.GetType() == typeof(Text)) != 3 || all.Count(p => p.GetType() == typeof(Line)) != 1 || all.Count(p => p.GetType() == typeof(Rectangle)) != 1)
        { throw new InvalidOperationException("Expected three S42 texts, one line and one rectangle."); }
        using var finder = new DMObjectsFinder(project);
        Function[] functions = finder.GetFunctions(null).Where(f => f.Name.IndexOf("-S42", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        NeedsFunction = functions.Length == 0;
        if (!NeedsFunction && (functions.Length != 1 || functions[0].Name != Tag || functions[0].Page.ToStringIdentifier() != page.ToStringIdentifier() || functions[0].ArticleReferences.Length != 0))
        { throw new InvalidOperationException("Reserved S42 function identity/part/page differs."); }
        if (!NeedsFunction && functions[0].PropertyPlacements.Count(IsTagPlacement) != 1) { throw new InvalidOperationException("Expected one 20214 property placement."); }
        if (NeedsFunction)
        {
            // A53 verified IEC_symbol/K; discover the actual project-owned variant before entering any write scope.
            SymbolLibrary library = project.SymbolLibraries.Single(l => l.Name == "IEC_symbol");
            Symbol symbol = library.Symbols.Single(s => s.Name == "K" && s.Type == Symbol.SymbolType.Function);
            _coil = symbol.Variants.OrderBy(v => v.VariantNr).First(v => v.ConnectionPoints.Length == 2);
            log("selected", new { library = library.Name, symbol = symbol.Name, variant = _coil.VariantNr,
                pins = _coil.ConnectionPoints.Select(p => new { location = XY(p.Location), direction = p.Direction.ToString() }).ToArray() });
        }
        var frameIds = new HashSet<string>(page.PlotFrame is { } frame ? Walk(frame.SubPlacements).Select(p => p.ToStringIdentifier()) : [], StringComparer.Ordinal);
        if (all.Any(p => frameIds.Contains(p.ToStringIdentifier()) || p.Page.ToStringIdentifier() != page.ToStringIdentifier()))
        { throw new InvalidOperationException("Movement closure includes a frame or another page."); }
        log("move_preflight", new { page = page.Name, needsFunction = NeedsFunction, groups = all.OfType<Group>().Select(g => g.ObjectIdentifier).ToArray() });
    }
    internal void Sentinel(Action<string, object> log)
    {
        var used = new HashSet<int>(Pages().Select(Counter));
        int counter = Enumerable.Range(9600, 99).First(c => !used.Contains(c) && !used.Contains(c + 1));
        Page page = NewPage(counter, "S42 sentinel");
        log("sentinel", new { counter, name = page.Name, oid = page.ObjectIdentifier, grid = page.GridSize });
    }
    internal void Create(Action<string, object> log)
    {
        Page page = NewPage(Counter(OwnedPages().Single(p => Description(p) == "S42 sentinel")) + 1, "S42 graphics");
        log("empty_page", PageGeometry(page));
        double grid = Grid(page);
        var returned = new List<Placement>();
        for (int i = 0; i < 3; i++)
        {
            using var contents = new MultiLangString();
            contents.AddString(i == 2 ? ISOCode.Language.L___ : ISOCode.Language.L_en_US, i == 0 ? "S42 single language" : i == 1 ? "S42 two languages" : "S42 independent");
            if (i == 1) { contents.AddString(ISOCode.Language.L_de_DE, "S42 zwei Sprachen"); }
            JObject expected = Languages(contents);
            var text = new Text();
            Stage = "Text.Create";
            text.Create(page, contents, 2.5);
            returned.Add(text);
            Stage = "Text.geometry";
            PointD target = new(12 * grid, (18 + 6 * i) * grid);
            text.Location = target;
            text.Justification = TextBase.JustificationType.BaseLineLeft;
            JObject state = PlacementData(text);
            log("text_created", new { requestedLanguages = expected, requestedLocation = XY(target), requestedHeight = 2.5, state });
            using MultiLangString actual = text.Contents;
            if (!JToken.DeepEquals(expected, Languages(actual)) || !Same(text.Location, target) || Math.Abs(text.Height - 2.5) > 1e-6 || text.Justification != TextBase.JustificationType.BaseLineLeft)
            { throw new InvalidOperationException("Text language/height/location/justification readback differs."); }
        }
        var line = new Line();
        Stage = "Line.Create";
        PointD start = new(12 * grid, 38 * grid), end = new(30 * grid, 42 * grid);
        line.Create(page, start, end);
        returned.Add(line);
        log("line_created", PlacementData(line));
        if (!Same(line.StartPoint, start) || !Same(line.EndPoint, end)) { throw new InvalidOperationException("Line endpoints differ."); }
        var rectangle = new Rectangle();
        Stage = "Rectangle.Create";
        rectangle.Create(page);
        returned.Add(rectangle);
        Stage = "Rectangle.SetArea";
        PointD lower = new(40 * grid, 18 * grid), upper = new(65 * grid, 32 * grid);
        rectangle.SetArea(lower, upper);
        log("rectangle_created", PlacementData(rectangle));
        if (!Same(rectangle.LowerLeftCorner, lower) || !Same(rectangle.UpperRightCorner, upper)) { throw new InvalidOperationException("Rectangle corners differ."); }
        Stage = "readback";
        log("inside_create", new { page = page.Name, geometry = PageGeometry(page), returned = returned.Select(PlacementData).ToArray() });
    }
    internal void Move(Action<string, object> log)
    {
        Page page = Drawing();
        double grid = Grid(page);
        Placement[] all = Walk(page.AllPlacements).OrderBy(p => p.ObjectIdentifier).ToArray();
        int row = 0;
        foreach (Placement item in all.Where(p => p.GetType() == typeof(Text) || p.GetType() == typeof(Line) || p.GetType() == typeof(Rectangle)))
        {
            JObject before = PlacementData(item);
            PointD target = new(20 * grid, (20 + 8 * row++) * grid);
            PointD current = item is Line currentLine ? currentLine.StartPoint : item.Location;
            if (Same(current, target)) { target = new PointD(target.X + 10 * grid, target.Y + 4 * grid); }
            Stage = "Placement.Location";
            if (item is Line line)
            {
                PointD end = new(target.X + line.EndPoint.X - line.StartPoint.X, target.Y + line.EndPoint.Y - line.StartPoint.Y);
                line.StartPoint = target;
                line.EndPoint = end;
                if (!Same(line.StartPoint, target) || !Same(line.EndPoint, end)) { throw new InvalidOperationException("Moved line endpoints differ."); }
            }
            else
            {
                item.Location = target;
                if (!Same(item.Location, target)) { throw new InvalidOperationException("Moved placement location differs."); }
            }
            JObject after = PlacementData(item);
            log("object_moved", new { requested = XY(target), setter = item is Line ? "StartPoint + EndPoint" : "Placement.Location", before, after, changedProperties = Changed(before, after) });
        }
        Function function;
        PropertyPlacement tag;
        if (NeedsFunction)
        {
            if (_coil is null) { throw new InvalidOperationException("Coil discovery must precede the write scope."); }
            Stage = "Function.Create";
            function = new Function();
            function.Create(page, _coil);
            function.Location = new PointD(75 * grid, 30 * grid);
            function.Name = Tag;
            tag = new PropertyPlacement();
            using AnyPropertyId id = 20214;
            tag.Create(function, id);
            // r4 screenshot: Location is relative to the parent symbol (absolute bounds = parent + Location); set the offset only.
            tag.Location = new PointD(4 * grid, 6 * grid);
            tag.Height = 2.5;
            tag.Justification = TextBase.JustificationType.BaseLineLeft;
            tag.IsVisible = true;
            if (!Same(tag.Location, new PointD(4 * grid, 6 * grid))) { throw new InvalidOperationException("20214 initial location differs."); }
        }
        else
        {
            function = all.OfType<Function>().Single(f => f.Name == Tag);
            tag = function.PropertyPlacements.Single(IsTagPlacement);
        }
        if (function.Name != Tag || function.FunctionCategory != FunctionCategory.Coil || function.ArticleReferences.Length != 0 || function.Pins.Length != 2)
        { throw new InvalidOperationException("Function identity/category/part/pin readback differs."); }
        JObject functionBefore = PlacementData(function), textBefore = PlacementData(tag);
        PointD oldParent = function.Location, oldText = tag.Location, destination = new(85 * grid, 45 * grid);
        if (Same(oldParent, destination)) { destination = new PointD(95 * grid, 55 * grid); }
        Stage = "Function.Location";
        function.Location = destination;
        PointD delta = new(destination.X - oldParent.X, destination.Y - oldParent.Y);
        bool follows = Same(tag.Location, new PointD(oldText.X + delta.X, oldText.Y + delta.Y));
        bool unchanged = Same(tag.Location, oldText);
        JObject functionAfter = PlacementData(function), textAfter = PlacementData(tag);
        log("function_moved", new { fixtureCreatedInThisStep = NeedsFunction, requested = XY(destination), delta = XY(delta), before = functionBefore, after = functionAfter,
            propertyBefore = textBefore, propertyAfter = textAfter, changedProperties = Changed(functionBefore, functionAfter), propertyChangedProperties = Changed(textBefore, textAfter),
            propertyFollowsParent = follows, propertyLocationUnchanged = unchanged,
            interpretation = Same(delta, new PointD(0, 0)) ? "zero movement; cannot classify coordinate semantics" : follows ? "PropertyPlacement.Location follows parent delta; inspect relative offset and bounds" : unchanged ? "PropertyPlacement.Location stayed absolute; compare bounds to determine rendered following" : "other; inspect evidence" });
        if (!Same(function.Location, destination) || tag.GetDisplayString().IndexOf("-S42K1", StringComparison.Ordinal) < 0)
        { throw new InvalidOperationException("Moved Function location or 20214 display differs."); }
        Group[] groups = all.OfType<Group>().ToArray();
        var childGroups = new HashSet<string>(groups.SelectMany(g => g.SubPlacements.OfType<Group>()).Select(g => g.ToStringIdentifier()), StringComparer.Ordinal);
        if (groups.Length == 0) { log("group_move", new { skipped = "No Group exists on the S42 page; no implicit sample creation." }); }
        foreach (Group group in groups.Where(g => !childGroups.Contains(g.ToStringIdentifier())))
        {
            JObject before = GroupData(group);
            Stage = "Group.Move";
            PointD vector = new(2 * grid, 3 * grid);
            group.Move(vector);
            log("group_moved", new { vector = XY(vector), before, after = GroupData(group) });
        }
        Stage = "readback";
        log("inside_move", new { page = page.Name, returnedFunction = PlacementData(function), returnedProperty = PlacementData(tag), state = Read() });
    }
    internal void View(Action<string, object> log)
    {
        Page page = Drawing();
        JObject before = Read();
        log("view_before", before);
        using var edit = new Edit();
        Page? opened = edit.OpenPageWithName(project.ProjectLinkFilePath, page.Name);
        log("editor_open", new { project = project.ProjectLinkFilePath, requestedPage = page.Name, returned = opened is null ? null : new { name = opened.Name, oid = opened.ObjectIdentifier, project = opened.Project.ProjectLinkFilePath } });
        JObject afterOpen = Read();
        bool redraw = edit.RedrawGed();
        log("editor_redraw", new { returned = redraw });
        JObject after = Read();
        bool openUnchanged = JToken.DeepEquals(before, afterOpen), unchanged = JToken.DeepEquals(before, after);
        log("view_after", after);
        log("view_check", new { openUnchanged, unchanged, noUndoScopeCreated = true, undoHistoryVerified = false, undoHistoryCheck = "Maintainer must execute one XEsUndoAction after this callback; it must undo the preceding write.", visualVerified = false });
        if (opened is null || opened.ToStringIdentifier() != page.ToStringIdentifier() || !redraw || !openUnchanged || !unchanged)
        { throw new InvalidOperationException("Editor return/read-only snapshot check failed; do not claim view passed."); }
    }
    private static bool IsTagPlacement(PropertyPlacement placement) { using AnyPropertyId id = placement.DisplayedProperty; return id.AsInt == 20214; }
    private static double Grid(Page page)
    {
        double grid = page.GridSize;
        if (double.IsNaN(grid) || double.IsInfinity(grid) || grid <= 0) { throw new InvalidOperationException("Invalid page grid."); }
        return grid;
    }
    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (UniversalPropertyList list in _lists.Values) { try { list.Dispose(); } catch (Exception ex) { failures.Add(ex); } }
        _lists.Clear();
        _coil = null;
        if (failures.Count > 0) { throw new AggregateException("Property-list release failed.", failures); }
    }
}
#pragma warning restore CA2000

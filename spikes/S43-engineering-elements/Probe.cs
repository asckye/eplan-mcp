using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

// A36/A39: one property list per object/callback, including returned objects; never dispose pins or StorableObjects.
#pragma warning disable CA2000 // Persistent/borrowed objects belong to EPLAN.
internal sealed partial class Probe(Project project, Action<string, object> report) : IDisposable
{
    private readonly PropertyListCache<UniversalPropertyList> _lists = new();
    private readonly List<SymbolReference> _returned = [];
    private readonly List<Choice> _choices = [];
    private readonly Dictionary<string, Choice> _selected = new(StringComparer.Ordinal);
    internal string Stage { get; private set; } = "preflight";
    private JObject? _lastSnapshot;
    internal void LogSnapshot(string phase, JObject snapshot)
    {
        if (_lastSnapshot is null) { report(phase, snapshot); }
        else { report(phase, new { format = "diff", changes = Evidence.Changes(_lastSnapshot, snapshot) }); }
        _lastSnapshot = snapshot;
    }
    private UniversalPropertyList Properties(StorableObject obj)
    {
        if (obj is null) { throw new InvalidOperationException("Cannot read properties of a null object."); }
        string? key = obj.ToStringIdentifier();
        if (string.IsNullOrEmpty(key)) { throw new InvalidOperationException("Cannot read properties without an object identifier."); }
        if (!_lists.TryGetValue(key!, out UniversalPropertyList? list))
        {
            list = obj switch { Page p => p.Properties, InterruptionPoint p => p.Properties, LocationBox b => b.Properties, ConnectionDefinitionPoint p => p.Properties,
                Function f => f.Properties, Symbol s => s.Properties, Connection c => c.Properties, _ => obj.Properties };
            if (list is null) { throw new InvalidOperationException("EPLAN returned a null property list."); }
            _lists.Add(key!, key!, list);
        }
        return list;
    }
    private string? Text(StorableObject obj, int property, int index = 0, bool multilingual = false)
    {
        using AnyPropertyId id = property;
        using PropertyValue? value = index == 0 ? Properties(obj)[id] : Properties(obj)[id, index];
        if (value is null || value.IsEmpty) { return null; }
        if (!multilingual) { return value.ToString(); }
        using MultiLangString? text = value.ToMultiLangString();
        return text?.GetStringToDisplay(ISOCode.Language.L_en_US);
    }
    private JToken? Value(StorableObject obj, int property, int index = 0, bool multilingual = false)
    {
        try { string? value = Text(obj, property, index, multilingual); return value is null ? null : new JValue(value); }
        catch (PropertyNotFoundException) { return new JObject { ["unavailableProperty"] = property }; } // Expected metadata absence, not a write failure.
    }
    private void Set(StorableObject obj, int property, string value, int index = 0)
    {
        TraceStage("property.set", new { property, index, requested = value, oid = obj.ObjectIdentifier });
        try
        {
            using AnyPropertyId id = property;
            // EplanPropertySetSession.Write uses this same owned-input lifetime, including translated values.
            using PropertyValue supplied = value;
            if (index == 0) { Properties(obj)[id] = supplied; } else { Properties(obj)[id, index] = supplied; }
            string? actual = Text(obj, property, index);
            report("property_readback", new { property, index, requested = value, actual, oid = obj.ObjectIdentifier });
            if (actual != value) { throw new InvalidOperationException("Property readback differs: " + Evidence.Number(property) + "; requested=" + value + "; actual=" + actual); }
        }
        catch (Exception ex)
        {
            report("property_error", new { property, index, requested = value, actual = Diagnose(() => Value(obj, property, index)),
                state = ObjectState(obj), error = Evidence.Error(ex) });
            throw;
        }
    }
    private void SetText(Function function, string value)
    {
        using AnyPropertyId id = 20011;
        using PropertyValue before = Properties(function)[id];
        using MultiLangString text = before.IsEmpty ? new MultiLangString() : before.ToMultiLangString();
        text.AddString(ISOCode.Language.L_en_US, value);
        // Same owned translated-value pattern as EplanPropertySetSession.Write; the setter copies the value.
        using PropertyValue supplied = text;
        Properties(function)[id] = supplied;
        if (Text(function, 20011, multilingual: true) != value) { throw new InvalidOperationException("Function text readback differs."); }
    }
    private int Counter(Page p) => int.TryParse(Text(p, 11012), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;
    private string Description(Page p) => Text(p, 11011, multilingual: true) ?? string.Empty;
    private Page[] Pages() => project.Pages.OrderBy(p => p.ObjectIdentifier).ToArray();
    private Page[] OwnedPages() => Pages().Where(p => Counter(p) is >= 9700 and <= 9799 && Description(p).StartsWith("S43 ", StringComparison.Ordinal)).ToArray();
    private Page SentinelPage()
    {
        Page[] found = OwnedPages().Where(p => Description(p) == "S43 sentinel").ToArray();
        if (found.Length != 1) { throw new InvalidOperationException("Expected one committed S43 sentinel; actual count=" + Evidence.Number(found.Length)); }
        return found[0];
    }
    private static int Offset(string group) => group switch { "interruption" => 1, "locationbox" => 3, "terminals" => 4, "cable" => 5, "plc" => 6, "contacts" => 7, "cable_wires" => 8, _ => throw new ArgumentException("Unknown element group.") };
    private Page[] GroupPages(string group) => OwnedPages().Where(p => Description(p) == "S43 " + group || Description(p) == "S43 " + group + " peer").ToArray();
    private Page RequireGroupPage(string group)
    {
        Page[] found = GroupPages(group);
        report("group_page_lookup", new { group, count = found.Length, pages = found.Select(p => new { p.ObjectIdentifier, p.Name }).ToArray() });
        if (found.Length != 1) { throw new InvalidOperationException("Expected one committed page for " + group + "; actual count=" + Evidence.Number(found.Length)); }
        return found[0];
    }
    private Page NewPage(int counter, string title)
    {
        using var name = new PagePropertyList();
        // All page structure and text is fictional; never copy the structure/text of an existing page.
        name.DESIGNATION_PLANT = "S43";
        name.DESIGNATION_LOCATION = "S43LAB";
        name.PAGE_COUNTER = Evidence.Number(counter);
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, name);
        using var text = new MultiLangString();
        text.AddString(ISOCode.Language.L_en_US, title);
        ((PagePropertyList)Properties(page)).PAGE_NOMINATIOMN = text;
        if (Counter(page) != counter || Description(page) != title || double.IsNaN(page.GridSize) || double.IsInfinity(page.GridSize) || page.GridSize <= 0)
        { throw new InvalidOperationException("Fresh page identity/grid readback differs."); }
        return page;
    }
    internal void Sentinel(Action<string, object> log)
    {
        if (OwnedPages().Length != 0) { throw new InvalidOperationException("S43 pages exist; reset the closed fixture or undo before sentinel."); }
        var used = new HashSet<int>(Pages().Select(Counter));
        int counter = Enumerable.Range(9700, 92).FirstOrDefault(c => Enumerable.Range(c, 9).All(n => !used.Contains(n)));
        if (counter == 0) { throw new InvalidOperationException("No nine consecutive free S43 counters remain in 9700-9799."); }
        Page p = NewPage(counter, "S43 sentinel");
        log("sentinel", new { page = p.Name, counter, oid = p.ObjectIdentifier });
    }
    internal void Preflight(string step, string group)
    {
        if (step == "sentinel") { if (OwnedPages().Length != 0) { throw new InvalidOperationException("S43 pages already exist."); } return; }
        _ = SentinelPage();
        if (step is "generate" or "bind" or "cores")
        {
            int expected = group == "interruption" ? 2 : 1;
            if (GroupPages(group).Length != expected) { throw new InvalidOperationException("Required committed element page(s) are missing."); }
            return;
        }
        if (step == "cable_line")
        {
            if (!_selected.ContainsKey("cable")) { throw new InvalidOperationException("No discovered cable definition symbol."); }
            Page page = RequireGroupPage("cable_wires");
            if (page.AllPlacements.OfType<Eplan.EplApi.DataModel.EObjects.Cable>().Any()) { throw new InvalidOperationException("Cable definition already exists on wires-first page."); }
            _ = CableRuns(page, "cable_wires"); // Requires three already committed, generated, explicitly identified runs.
            return;
        }
        string[] roles = step switch { "interruption" => ["interruption", "two_pole"], "locationbox" => ["locationbox", "two_pole"],
            "terminals" => ["terminal", "two_pole"], "cable" => ["cable", "two_pole"], "cable_wires" => ["two_pole"], "plc" => ["plc_box", "plc_di"], "contacts" => ["no", "nc"], _ => [] };
        foreach (string role in roles) { if (!_selected.ContainsKey(role)) { throw new InvalidOperationException("No discovered candidate for " + role + "; inspect discover. No substitute/import."); } }
        int counter = Counter(SentinelPage()) + Offset(step);
        if (counter is < 9700 or > 9799) { throw new InvalidOperationException("Reserved counter exceeds the S43 range; reset the closed fixture."); }
        if (Pages().Any(p => Counter(p) == counter || (step == "interruption" && Counter(p) == counter + 1)))
        { throw new InvalidOperationException("Reserved element page exists; do not repeat placement."); }
        using var finder = new DMObjectsFinder(project);
        // Only compare names; no page texts or customer property data enter the inventory.
        if (finder.GetFunctions(null).Any(f => f.Name.IndexOf("S43", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (f.Page is null || !OwnedPages().Any(p => p.ObjectIdentifier == f.Page.ObjectIdentifier))) ||
            (step == "interruption" && finder.GetInterruptionPoints(null).Any(IsS43Link)))
        { throw new InvalidOperationException("Reserved fictional identifiers exist outside this group's new pages."); }
    }
    internal void GenerateOnly(string group, Action<string, object> log)
    {
        Page[] pages = GroupPages(group);
        log("generate_targets", pages.Select(p => new { p.ObjectIdentifier, p.Name }).ToArray());
        using (var generate = new Generate()) { generate.Connections(pages, false); } // A52: only after placement committed, page-scoped.
        if (group is "cable" or "cable_wires") { foreach (Page page in pages) { log("cable_after_generate", CablePageData(page)); } }
    }
    private static JObject XY(PointD p) => new() { ["x"] = p.X, ["y"] = p.Y };
    private static bool Same(PointD a, PointD b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
    internal void BeginWrite()
    {
        // Only S43 placements/connections can be written; symbol and page lists already cached are baseline too.
        Page[] pages = OwnedPages();
        var owners = new HashSet<string>(pages.Select(p => p.ToStringIdentifier()), StringComparer.Ordinal);
        foreach (Placement placement in pages.SelectMany(p => p.AllPlacements)) { owners.Add(placement.ToStringIdentifier()); }
        using var finder = new DMObjectsFinder(project);
        var pageIds = new HashSet<long>(pages.Select(p => p.ObjectIdentifier));
        foreach (Connection connection in finder.GetConnections(null).Where(c => c.Page is not null && pageIds.Contains(c.Page.ObjectIdentifier)))
        { owners.Add(connection.ToStringIdentifier()); }
        _lists.BeginWrite(owners);
    }
    internal void PrepareRollback()
    {
        // Must be called while all new objects still exist. No native object/property snapshot is allowed after Abort.
        var cleanup = _lists.PrepareRollback();
        _returned.Clear();
        report("before_rollback_cache_cleanup", new { released = cleanup.Released, errors = cleanup.Errors.Select(Evidence.Error).ToArray(), beforeNativeDeletion = true });
        if (cleanup.Errors.Length > 0) { throw new AggregateException("Pre-rollback property cleanup failed; wrappers were removed and finalizers suppressed before native deletion.", cleanup.Errors); }
    }
    internal void EndWrite() => _lists.EndWrite();
    public void Dispose()
    {
        _returned.Clear();
        _lists.Dispose();
    }
}
#pragma warning restore CA2000

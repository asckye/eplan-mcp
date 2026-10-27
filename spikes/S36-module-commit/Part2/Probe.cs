using System.Globalization;
using System.Xml.Linq;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Spikes.S36.Part2;

// A36/A39/A55: one property list per owner/callback; never dispose StorableObjects or pins.
#pragma warning disable CA2000 // Persistent objects belong to EPLAN, including objects created by this probe.
internal sealed partial class Probe(Project project, Action<string, object> log) : IDisposable
{
    internal const int SentinelCounter = 938;
    internal const int SourceCounter = 939;
    internal const int TargetCounter = 940;
    private const string HolderName = "S36P2";
    private readonly PropertyListCache<UniversalPropertyList> _lists = new();
    private readonly HashSet<(int Id, int Index)> _descriptors = [(20093, 0), (20011, 0), (20100, 1), (20027, 0), (11011, 0), (10901, 1000)];
    private string? _sample;
    private string[] _parts = [];
    internal string? OutputFolder { get; private set; }
    internal string Stage { get; private set; } = "preflight";
    internal bool OutcomeUnknown { get; private set; }
    internal void SetStage(string stage) { Stage = stage; log("stage", new { stage }); }
    internal void MarkOutcomeUnknown() { OutcomeUnknown = true; _lists.Abandon(); }
    private static readonly (string Variable, int Id, int Index)[] _bindings =
        [("Boolean", 20093, 0), ("Multilingual", 20011, 0), ("Part", 20100, 1), ("Plain", 20027, 0)];
    private UniversalPropertyList Properties(StorableObject obj)
    {
        string key = obj.ToStringIdentifier() ?? throw new InvalidOperationException("Object has no identifier.");
        if (!_lists.TryGetValue(key, out UniversalPropertyList? list))
        {
            list = obj switch { Page p => p.Properties, Function f => f.Properties, Project p => p.Properties,
                PlaceHolder h => h.Properties, _ => obj.Properties };
            if (list is null) { throw new InvalidOperationException("EPLAN returned a null property list."); }
            _lists.Add(key, key, list);
        }
        return list;
    }
    private static AnyPropertyId Id(int id, int index = 0)
    {
        AnyPropertyId result = id;
        result.Index = checked((short)index);
        return result;
    }
    private PropertyValue? Property(StorableObject obj, int id, int index = 0)
    {
        using AnyPropertyId property = id;
        if (obj is Page && id == 11011) { return ((PagePropertyList)Properties(obj)).PAGE_NOMINATIOMN; }
        return index == 0 ? Properties(obj)[property] : Properties(obj)[property, index];
    }
    private static MultiLangString Text(string text, ISOCode.Language language = ISOCode.Language.L___)
    {
        var result = new MultiLangString();
        result.AddString(language, text);
        return result;
    }
    private int Counter(Page page)
    {
        using PropertyValue? value = ((PagePropertyList)Properties(page)).PAGE_COUNTER;
        return value is not null && !value.IsEmpty && int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;
    }
    private Page? Find(int counter) => project.Pages.SingleOrDefault(p => Counter(p) == counter);
    private Page Require(int counter) => Find(counter) ?? throw new InvalidOperationException("Required page is missing: " + Evidence.Number(counter));
    private static PlaceHolder Holder(Page page) => page.AllPlacements.OfType<PlaceHolder>().Single(h => h.Name == HolderName);
    private static Function Target(Page page) => page.Functions.Where(f => f is not null && f.IsMainFunction).OrderBy(f => f.ObjectIdentifier).First();
    private Page NewPage(int counter, string title)
    {
        // Same Circuit template structure as part 1; only the reserved counter changes.
        Page template = project.Pages.First(p => p.PageType == DocumentTypeManager.DocumentType.Circuit);
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
            using AnyPropertyId property = part;
            using PropertyValue? value = Properties(template)[property];
            if (value is not null && !value.IsEmpty) { name[property] = value; }
        }
        name.PAGE_COUNTER = Evidence.Number(counter);
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, name);
        using MultiLangString description = Text(title, ISOCode.Language.L_en_US);
        ((PagePropertyList)Properties(page)).PAGE_NOMINATIOMN = description;
        return page;
    }
    internal void Discover()
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        _sample = PathMap.SubstitutePath(@"$(MD_MACROS)\EPLAN_Macro\202_Power_Supply_24V\101_01_Variant_1\24VDC_Supply.ema");
        // Supplement ExistingIds with EVERY binding descriptor in the shipped file, including empty/indexed bindings.
        // This is a fixed-sample probe, not a general-purpose proof for arbitrary placeholder formats.
        Evidence.NoLinks(_sample);
        foreach (XElement entry in XDocument.Load(_sample).Descendants("S115x1904"))
        {
            _descriptors.Add((int.Parse(entry.Attribute("A1911")!.Value, CultureInfo.InvariantCulture),
                int.Parse(entry.Attribute("A1912")!.Value, CultureInfo.InvariantCulture)));
        }
        _parts = project.Articles.Select(a => a?.PartNr).Where(p => !string.IsNullOrEmpty(p)).Cast<string>()
            .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (_parts.Length < 2) { throw new InvalidOperationException("Need at least two existing project parts; no master-data fallback."); }
        log("discovered", new { sample = "24VDC_Supply.ema", representation = "MultiLine", variant = 1, numbering = "Number",
            descriptors = _descriptors.OrderBy(d => d.Id).ThenBy(d => d.Index).Select(d => new { propertyId = d.Id, index = d.Index }).ToArray(), projectParts = _parts.Length });
    }
    internal void Preflight(string step)
    {
        SetStage("preflight." + step);
        if (step == "sentinel")
        {
            if (new[] { SentinelCounter, SourceCounter, TargetCounter }.Any(c => Find(c) is not null)) { throw new InvalidOperationException("Reserved S36P2 pages already exist."); }
            return;
        }
        Page sentinel = Require(SentinelCounter);
        using PropertyValue? title = Property(sentinel, 11011);
        using MultiLangString? text = title is null || title.IsEmpty ? null : title.ToMultiLangString();
        if (text?.GetStringToDisplay(ISOCode.Language.L_en_US) != "S36P2 sentinel") { throw new InvalidOperationException("The reserved page is not the unchanged S36P2 sentinel."); }
        if (step == "insert")
        {
            if (Find(SourceCounter) is not null || Find(TargetCounter) is not null) { throw new InvalidOperationException("Do not repeat insertion."); }
        }
        else
        {
            _ = Require(SourceCounter);
            if (step == "writeback" && (Find(TargetCounter) is not null || Require(SourceCounter).AllPlacements.OfType<PlaceHolder>().Any(h => h.Name == HolderName)))
            { throw new InvalidOperationException("Writeback objects already exist; undo or reset the closed fixture."); }
        }
    }
    internal void BeginWrite()
    {
        using var finder = new DMObjectsFinder(project);
        IEnumerable<StorableObject> owners = new StorableObject[] { project }.Concat(project.Pages)
            .Concat(project.Pages.SelectMany(p => p.AllPlacements ?? [])).Concat(finder.GetFunctions(null) ?? []);
        _lists.BeginWrite(owners.Where(o => o is not null).Select(o => o.ToStringIdentifier()));
    }
    internal void PrepareRollback()
    {
        var released = _lists.PrepareRollback(); // MUST complete before SafetyPoint.Rollback/Transaction.Abort (A55).
        log("cache_before_rollback", new { released = released.Released, errors = released.Errors.Select(Evidence.Error).ToArray() });
        if (released.Errors.Length > 0) { throw new AggregateException("Pre-rollback property cleanup failed.", released.Errors); }
    }
    internal void EndWrite() => _lists.EndWrite();
    internal void Sentinel() => NewPage(SentinelCounter, "S36P2 sentinel");
    internal void Insert()
    {
        Page page = NewPage(SourceCounter, "S36P2 source");
        using var source = new WindowMacro();
        source.Open(_sample ?? throw new InvalidOperationException("Discover first."), project);
        using var insert = new EplanInsert();
        StorableObject[] inserted = insert.WindowMacro(source, WindowMacro.Enums.RepresentationType.MultiLine, 1, page,
            new PointD(100, 150), EplanInsert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.Number);
        log("inserted", new { objects = inserted?.Length, page = page.Name, holders = page.AllPlacements.OfType<PlaceHolder>().Count() });
        if (inserted is not { Length: > 0 }) { throw new InvalidOperationException("No sample objects inserted."); }
        Function target = Target(page);
        var properties = (FunctionPropertyList)Properties(target);
        properties.FUNC_SUPPRESSINDEVICELIST = false;
        using PropertyValue? existing = Property(target, 20011);
        using MultiLangString initial = existing is null || existing.IsEmpty ? new MultiLangString() : existing.ToMultiLangString();
        initial.AddString(ISOCode.Language.L_en_US, "S36P2 English before");
        initial.AddString(ISOCode.Language.L_de_DE, "S36P2 Deutsch behalten");
        initial.AddString(ISOCode.Language.L_zh_CN, "S36P2 Chinese retained");
        properties.FUNC_TEXT = initial;
        properties.FUNC_TECHNICAL_CHARACTERISTIC = "S36P2 plain before";
        _ = BuildHolder(page, "S36P2Guard", external: true);
    }
    public void Dispose() => _lists.Dispose();
}

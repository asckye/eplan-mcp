using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Newtonsoft.Json.Linq;
using Direction = Eplan.EplApi.DataModel.PinBase.Directions;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    private FunctionDefinition? _plcDiDefinition;
    private sealed class Choice(SymbolVariant variant, Symbol.SymbolType type, FunctionCategory? category, string definition,
        JObject info, PinBase[] pins, PointD[] locations, Direction[] directions, bool validPins, int variantNumber)
    {
        internal readonly SymbolVariant Variant = variant;
        internal readonly Symbol.SymbolType Type = type;
        internal readonly FunctionCategory? Category = category;
        internal readonly string Definition = definition;
        internal readonly JObject Info = info;
        internal readonly PinBase[] Pins = pins;
        internal readonly PointD[] Locations = locations;
        internal readonly Direction[] Directions = directions;
        internal readonly bool ValidPins = validPins;
        internal readonly int VariantNumber = variantNumber;
    }
    private static bool Vertical(Choice c) => c.ValidPins && c.Pins.Length == 2 && c.Directions.Contains(Direction.Up) &&
        c.Directions.Contains(Direction.Down) && Math.Abs(c.Locations[0].X - c.Locations[1].X) < 1e-6;
    private static bool DigitalInput(string? text) => text is not null && Regex.IsMatch(text, @"\bDI\b|digital\s+input|input.*digital", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string DefinitionText(FunctionDefinition? definition, ReadRecord? record = null)
    {
        if (definition is null) { return string.Empty; }
        record ??= new ReadRecord();
        using MultiLangString? name = record.Get<MultiLangString?>("definition.name", () => definition.Name);
        using MultiLangString? group = record.Get<MultiLangString?>("definition.groupName", () => definition.GroupName);
        string? groupText = record.Get<string?>("definition.groupText", () => group?.GetStringToDisplay(ISOCode.Language.L_en_US));
        string? nameText = record.Get<string?>("definition.nameText", () => name?.GetStringToDisplay(ISOCode.Language.L_en_US));
        return (groupText ?? string.Empty) + ": " + (nameText ?? string.Empty);
    }
    internal void Discover(Action<string, object> log, bool inventory = true)
    {
        var read = new ReadRecord();
        _choices.Clear();
        _selected.Clear();
        _plcDiDefinition = null;
        try { DiscoverCore(log, inventory, read); }
        catch (Exception ex)
        {
            // Incomplete discovery is evidence, not a crash. Preflight still refuses missing required selections.
            _choices.Clear();
            _selected.Clear();
            _plcDiDefinition = null;
            read.Error("discovery", ex);
            log("discovery_incomplete", read.Data);
        }
    }
    private void DiscoverCore(Action<string, object> log, bool inventory, ReadRecord read)
    {
        using var quiet = read.Get<QuietModeStep?>("quiet", () => new QuietModeStep(QuietModes.ShowNoDialogs));
        using var finder = read.Get<DMObjectsFinder?>("finder", () => new DMObjectsFinder(project));
        var used = new Dictionary<(string Library, string Symbol), HashSet<FunctionCategory>>();
        Function[]? functions = read.Get<Function[]?>("existingFunctions", () => finder?.GetFunctions(null));
        read.Field("existingFunctionCount", () => functions?.Length);
        int skippedFunctions = 0;
        foreach (Function? function in functions ?? [])
        {
            var item = new ReadRecord();
            bool? placed = item.Get<bool?>("isPlaced", () => function?.IsPlaced);
            if (placed == false) { continue; }
            SymbolVariant? variant = item.Get<SymbolVariant?>("symbolVariant", () => function?.SymbolVariant);
            FunctionDefinition? definition = item.Get<FunctionDefinition?>("functionDefinition", () => function?.FunctionDefinition);
            string? library = item.Get<string?>("library", () => variant?.SymbolLibraryName);
            string? symbol = item.Get<string?>("symbol", () => variant?.SymbolName);
            FunctionCategory? category = item.Get<FunctionCategory?>("category", () => function?.FunctionCategory);
            if (function is null || placed is null || definition is null || string.IsNullOrEmpty(library) || string.IsNullOrEmpty(symbol) || category is null)
            {
                item.Field("oid", () => function?.ObjectIdentifier);
                item.Field("runtimeType", () => function?.GetType().FullName);
                item.Field("isPlaced", () => placed);
                item.Field("library", () => library);
                item.Field("symbol", () => symbol);
                item.Field("functionDefinition", () => definition is null ? null : definition.ObjectIdentifier);
                item.Field("category", () => category?.ToString());
                item.Field("reason", () => "Missing placed-function symbol key/category/definition; excluded from existing-use map");
                log("discovery_function_skipped", item.Data);
                skippedFunctions++;
                continue;
            }
            var key = (library!, symbol!);
            if (!used.TryGetValue(key, out HashSet<FunctionCategory>? categories)) { categories = []; used.Add(key, categories); }
            categories.Add(category.Value);
        }
        var watch = Stopwatch.StartNew();
        SymbolLibrary[]? libraries = read.Get<SymbolLibrary[]?>("symbolLibraries", () => project.SymbolLibraries);
        read.Field("symbolLibraryCount", () => libraries?.Length);
        foreach (SymbolLibrary? library in libraries ?? [])
        {
            var libraryRead = new ReadRecord();
            string? libraryName = libraryRead.Get<string?>("name", () => library?.Name);
            libraryRead.Field("name", () => libraryName);
            if (library is null || string.IsNullOrEmpty(libraryName)) { log("discovery_library_skipped", libraryRead.Data); continue; }
            bool inspect = libraryName == "SPECIAL" || libraryName!.StartsWith("IEC", StringComparison.Ordinal);
            log("library", new { name = libraryName, inspected = inspect, ownership = "Project.SymbolLibraries; no Initialize/Close/Dispose" });
            if (!inspect) { continue; }
            Symbol[]? symbols = libraryRead.Get<Symbol[]?>("symbols", () => library.Symbols);
            libraryRead.Field("symbolCount", () => symbols?.Length);
            if (symbols is null) { log("discovery_library_skipped", libraryRead.Data); continue; }
            foreach (Symbol? symbol in symbols)
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) { throw new TimeoutException("Discovery exceeded 90 s; inventory incomplete."); }
                var metadata = new ReadRecord();
                string? symbolName = metadata.Get<string?>("symbol", () => symbol?.Name);
                Symbol.SymbolType? type = metadata.Get<Symbol.SymbolType?>("type", () => symbol?.Type);
                metadata.Field("library", () => libraryName);
                metadata.Field("symbol", () => symbolName);
                metadata.Field("type", () => type?.ToString());
                if (symbol is null || string.IsNullOrEmpty(symbolName) || type is null)
                { log("discovery_symbol_skipped", metadata.Data); continue; }
                if (type is not (Symbol.SymbolType.Function or Symbol.SymbolType.BoxFunction or Symbol.SymbolType.InterruptionPoint or Symbol.SymbolType.LocationBox or Symbol.SymbolType.CableDefinitionLine)) { continue; }
                JToken? encoded = metadata.Get<JToken?>("definitionCode", () => Value(symbol, 20188));
                string? definitionText = metadata.Get<string?>("definitionText", () => (Value(symbol, 20117, multilingual: true) as JValue)?.Value as string);
                string definition = definitionText ?? string.Empty;
                JToken? number = metadata.Get<JToken?>("number", () => Value(symbol, 16002));
                FunctionCategory? category = null;
                string source = "unavailable";
                if (used.TryGetValue((libraryName!, symbolName!), out HashSet<FunctionCategory>? categories) && categories.Count == 1)
                { category = categories.First(); source = "existing Function.FunctionCategory; names/texts not copied"; }
                else if (encoded is JValue && int.TryParse(encoded.Value<string>()?.Split('/')[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && Enum.IsDefined(typeof(FunctionCategory), (FunctionCategory)n))
                { category = (FunctionCategory)n; source = "20188 category prefix; verify created native type/category"; }
                SymbolVariant[]? variants = metadata.Get<SymbolVariant[]?>("variants", () => symbol.Variants);
                if (variants is null) { log("discovery_symbol_skipped", metadata.Data); continue; }
                foreach (SymbolVariant? variant in variants)
                {
                    var info = new ReadRecord();
                    int? variantNumber = info.Get<int?>("variant", () => variant?.VariantNr);
                    info.Field("library", () => libraryName);
                    info.Field("symbol", () => symbolName);
                    info.Field("variant", () => variantNumber);
                    if (variant is null || variantNumber is null) { log("discovery_variant_skipped", info.Data); continue; }
                    PinBase[]? pins = info.Get<PinBase[]?>("connectionPoints", () => variant.ConnectionPoints);
                    var locations = new List<PointD>();
                    var directions = new List<Direction>();
                    bool validPins = pins is not null;
                    info.Array("pins", () => pins, pin =>
                    {
                        var data = new ReadRecord();
                        PointD? location = data.Get<PointD?>("location", () => pin.Location);
                        Direction? direction = data.Get<Direction?>("direction", () => pin.Direction);
                        data.Field("location", () => location is PointD p ? XY(p) : null);
                        data.Field("direction", () => direction?.ToString());
                        if (location is PointD p && direction is Direction d) { locations.Add(p); directions.Add(d); }
                        else { validPins = false; }
                        return data.Data;
                    });
                    validPins &= pins is not null && pins.All(p => p is not null) && locations.Count == pins.Length;
                    info.Field("library", () => libraryName);
                    info.Field("symbol", () => symbolName);
                    info.Field("number", () => number?.DeepClone());
                    info.Field("type", () => type.Value.ToString());
                    info.Field("variant", () => variantNumber);
                    info.Field("category", () => category?.ToString());
                    info.Field("categorySource", () => source);
                    info.Field("definitionCode", () => encoded?.DeepClone());
                    info.Field("definitionText", () => definitionText);
                    if (metadata.Data["readErrors"] is JToken errors) { info.Data["symbolReadErrors"] = errors.DeepClone(); }
                    var choice = new Choice(variant, type.Value, category, definition, info.Data, (pins ?? []).OfType<PinBase>().ToArray(),
                        locations.ToArray(), directions.ToArray(), validPins, variantNumber.Value);
                    bool plcRelated = category?.ToString().IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0 || definition.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (type == Symbol.SymbolType.Function && !Vertical(choice) && !plcRelated) { continue; }
                    _choices.Add(choice);
                    if (inventory) { log("symbol_candidate", info.Data); }
                }
            }
        }
        // Preserve deterministic choices without re-reading possibly-null native sorting keys.
        _choices.Sort((a, b) =>
        {
            int result = StringComparer.Ordinal.Compare((string?)a.Info["library"], (string?)b.Info["library"]);
            if (result == 0) { result = StringComparer.Ordinal.Compare((string?)a.Info["symbol"], (string?)b.Info["symbol"]); }
            return result == 0 ? a.VariantNumber.CompareTo(b.VariantNumber) : result;
        });
        void Select(string role, Func<Choice, bool> predicate)
        {
            Choice? choice = _choices.FirstOrDefault(predicate);
            if (choice is not null) { _selected.Add(role, choice); }
            log("selected", new { role, selected = choice?.Info, missing = choice is null });
        }
        Select("two_pole", c => c.Category == FunctionCategory.Coil && Vertical(c));
        Select("interruption", c => c.Type == Symbol.SymbolType.InterruptionPoint && c.ValidPins && c.Pins.Length == 1 && c.Directions[0] is Direction.Up or Direction.Down);
        Select("locationbox", c => c.Type == Symbol.SymbolType.LocationBox);
        Select("terminal", c => c.Category == FunctionCategory.Terminal && Vertical(c));
        Select("cable", c => c.Type == Symbol.SymbolType.CableDefinitionLine && c.VariantNumber is 0 or 2);
        Select("plc_box", c => c.Category == FunctionCategory.PLCBox);
        TraceStage("discover.plc_definitions");
        var digitalDefinitions = new List<(FunctionDefinition Definition, int Pins, JObject Info)>();
        var definitionLibrary = read.Get("functionDefinitionLibrary", () => project.FunctionDefinitionLibrary);
        FunctionDefinition[]? definitions = read.Get<FunctionDefinition[]?>("functionDefinitions", () => definitionLibrary?.FunctionDefinitions);
        read.Field("functionDefinitionCount", () => definitions?.Length);
        foreach (FunctionDefinition? definition in definitions ?? [])
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(90)) { throw new TimeoutException("PLC definition discovery exceeded 90 s."); }
            var item = new ReadRecord();
            FunctionCategory? category = item.Get<FunctionCategory?>("category", () => definition?.FunctionCategory);
            if (definition is null || category is null) { log("discovery_definition_skipped", item.Data); continue; }
            if (category is not (FunctionCategory.PLCTerminal or FunctionCategory.PLCBox)) { continue; }
            string text = DefinitionText(definition, item);
            Pin[]? pins = item.Get<Pin[]?>("connectionPoints", () => definition.ConnectionPoints);
            short? group = item.Get<short?>("Group", () => definition.Group), id = item.Get<short?>("Id", () => definition.Id);
            bool digitalInput = category == FunctionCategory.PLCTerminal && DigitalInput(text) && pins?.Length is 1 or 2 && pins.All(p => p is not null) && group is not null && id is not null;
            item.Field("category", () => category?.ToString());
            item.Field("Group", () => group);
            item.Field("Id", () => id);
            item.Field("text", () => text);
            item.Field("pins", () => pins?.Length);
            item.Field("digitalInput", () => digitalInput);
            if (inventory) { log("plc_definition_candidate", item.Data); }
            if (digitalInput) { digitalDefinitions.Add((definition, pins!.Length, item.Data)); }
        }
        bool DiSymbol(Choice c) => c.Type == Symbol.SymbolType.Function && c.ValidPins && c.Pins.Length is 1 or 2 &&
            (c.Category == FunctionCategory.PLCTerminal || c.Definition.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0) && DigitalInput(c.Definition);
        Choice? dedicated = _choices.FirstOrDefault(DiSymbol);
        Choice? generic = null;
        JObject? selectedDefinition = null;
        foreach (var entry in digitalDefinitions)
        {
            if (dedicated is not null)
            {
                if (entry.Pins == dedicated.Pins.Length) { _plcDiDefinition = entry.Definition; selectedDefinition = entry.Info; break; }
                continue;
            }
            generic = _choices.Where(c => c.Type == Symbol.SymbolType.Function && c.Category == FunctionCategory.PLCTerminal && c.ValidPins &&
                c.Pins.Length == entry.Pins && c.Definition.IndexOf("bus", StringComparison.OrdinalIgnoreCase) < 0 && c.Definition.IndexOf("network", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderByDescending(c => c.Definition.IndexOf("general", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
            if (generic is not null) { _plcDiDefinition = entry.Definition; selectedDefinition = entry.Info; break; }
        }
        Choice? di = dedicated ?? generic;
        if (di is not null) { _selected.Add("plc_di", di); }
        log("selected", new { role = "plc_di", selected = di?.Info, missing = di is null, definition = selectedDefinition,
            mapping = dedicated is not null ? "Dedicated digital-input symbol; verify native definition" : "Generic PLC graphical variant plus discovered native digital-input definition; never label a general definition as DI" });
        Select("no", c => c.Category == FunctionCategory.NoContact && Vertical(c));
        Select("nc", c => c.Category == FunctionCategory.NcContact && Vertical(c));
        log("discovery_summary", new { inventory, variants = _choices.Count, skippedFunctions,
            categories = _choices.GroupBy(c => c.Category?.ToString() ?? c.Type.ToString()).ToDictionary(g => g.Key, g => g.Count()), digitalDefinitions = digitalDefinitions.Count,
            readEvidence = read.Data });
        log("catalog_limits", new { typeAndSignatureOnly = true, visualVerified = false,
            unknown = new JArray("Native factory result and box inheritance", "Interruption pairing/recalculation timing", "Template-to-wire mapping and side effects", "PLC native table layout and digital input semantics"),
            readOnly = new JArray(20028, 24300, 24000, 20403), writablePinCandidate = "20038 semicolon / paragraph list -> Pin.Designation; log 20022[index] too",
            coreColor = "31004 string; 31005 is integer graphical autoconnect color, never a BK/BU/GNYE string" });
    }
}

using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

// Read-only inventory, using the same LiveEplan fixture as SampleProjectInventorySpikeTests.
// No project open, navigation, generation, transaction or write tool is used here.
[Trait("Size", "Large")]
[Trait("Spike", "Inventory")]
public sealed class SmallProjectInventorySpikeTests(LiveEplan eplan)
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };
    private static readonly JsonElement _null = JsonSerializer.SerializeToElement<object?>(null);
    // Exact inheritance in catalog/eplan-2026.0.3/types.jsonl; property IDs alone also match 3D placements.
    private static readonly HashSet<string> _functionKinds = new(StringComparer.Ordinal)
    {
        "BoxedDevice", "ConnectionDefinitionPoint", "BusTerminal", "Cable", "CableUnit", "Harness", "PLC",
        "Plug", "PlugStrip", "Shield", "Terminal", "TerminalStrip", "Function", "Segment",
    };
    private static readonly HashSet<string> _otherSymbolKinds = new(StringComparer.Ordinal)
    {
        "DeletedObjectInfo", "DynamicConnectionLine", "FunctionBase", "InterruptionPoint", "LocationBox", "MacroBox",
        "NetDefinitionPoint", "PipingDefinition", "SegmentPlacement", "PotentialDefinition", "StrandConnector",
        "StrandInterConnector", "SymbolReference",
    };
    private static readonly (string Name, string Type)[] _pages =
    [
        ("&AAA1/1", "TitlePage"), ("&AAB1/1", "TableOfContents"),
        ("=GAA+A1&EFS1/1", "Circuit"), ("=GAA+A1&EFS1/2", "Circuit"),
        ("=K1+A2&EFS1/1", "Circuit"), ("=K1+A2&EFS1/2", "Circuit"),
        ("=K1+A2&EFA1/1", "Overview"), ("=K1+A2&EFA1/2", "Overview"),
        ("=K1+A2&EFA1/3", "Overview"), ("=K1+A2&EFA1/4", "Overview"),
        ("=K1+A2&EFA1/5", "Overview"), ("=K1+A2&EFA1/6", "PLCDiagram"),
        ("=K1+A2&EFA1/7", "PLCDiagram"), ("+A1&EMA2/2", "TerminalDiagram"),
        ("+A1&EPA1/1", "PartsSumList"), ("+A1&EMB1/1", "CableOverview"),
    ];

    [Fact]
    public async Task SelectedPages_AllReadableObjects_WritesTheInventory()
    {
        string project = eplan.ProjectPath;
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        string file = Path.Combine(folder, "small-project-inventory.json");
        var pages = new List<object>();
        var symbols = new Dictionary<(string Library, string Name), ReadRows>();
        foreach ((string name, string expectedType) in _pages)
        {
            pages.Add(await InventoryPageAsync(project, name, expectedType, symbols));
            // Preserve completed pages if a later transport failure or cancellation interrupts the run.
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(pages, _indented), TestContext.Current.CancellationToken);
        }
        TestContext.Current.TestOutputHelper!.WriteLine("Inventory: " + file);
    }

    private async Task<object> InventoryPageAsync(string project, string name, string expectedType,
        Dictionary<(string Library, string Name), ReadRows> symbolCache)
    {
        var gaps = new List<string>();
        var args = new Dictionary<string, object?> { ["project"] = project, ["page"] = name };
        ReadRows drawings = await ReadAllAsync("eplan_page_get", "items", args, gaps, name,
            container: "drawings", cursorArgument: "drawing_cursor", limitArgument: "drawing_limit",
            extra: new() { ["include_drawings"] = true, ["response_format"] = "detailed" });
        JsonElement page = Member(drawings.First, "page");
        string pageType = Text(page, "type") ?? expectedType;
        if (Text(page, "type") is { } actual && actual != expectedType)
        {
            gaps.Add($"Expected page type {expectedType}; reader returned {actual}.");
        }
        if (Flag(drawings.First, "truncated"))
        {
            gaps.Add("page_get left out device/property rows; object_find/get and device_list are recorded separately.");
        }

        var objects = new Dictionary<string, InventoryObject>(StringComparer.Ordinal);
        var readers = new Dictionary<string, object>(StringComparer.Ordinal);
        // The published type catalog includes graphics beyond the narrower drawing reader.
        // BlackBox and PLCTerminal are query aliases, not additional runtime objects (A60).
        foreach (string kind in ObjectReadTypes.Types.Where(k => k is not ("BlackBox" or "PLCTerminal")))
        {
            if (!ObjectReadTypes.Supported(kind))
            {
                string reason = ObjectReadTypes.UnsupportedReason(kind);
                readers[kind] = new { status = "unsupported", reason, count = (int?)null };
                gaps.Add($"{kind} is not readable through object_find/get: {reason}.");
                continue;
            }
            ReadRows found = await ReadAllAsync("eplan_object_find", "items",
                new(args) { ["object_type"] = kind }, gaps, name + "/" + kind, cursorField: "cursor");
            readers[kind] = new { status = found.Complete ? "complete" : "partial", count = found.Items.Count };
            foreach (JsonElement item in found.Items)
            {
                JsonElement locator = Member(item, "locator");
                string? key = Identity(locator);
                if (key is null)
                {
                    gaps.Add($"{kind}: discovery row has no readable locator.");
                    continue;
                }
                if (objects.ContainsKey(key))
                {
                    continue;
                }
                ReadRows properties = await ReadAllAsync("eplan_object_get", "items",
                    new() { ["project"] = project, ["object_type"] = kind, ["locator"] = locator, ["response_format"] = "detailed" },
                    gaps, name + "/" + kind + "/" + key, cursorField: "cursor");
                foreach (IGrouping<string, JsonElement> unread in properties.Items.Where(p => Text(p, "status") is "read_refused" or "not_applicable")
                    .GroupBy(p => Text(p, "status")!))
                {
                    gaps.Add($"{kind}/{key}: {unread.Count()} properties are {unread.Key}; IDs and reasons are retained in the object properties.");
                }
                objects.Add(key, new InventoryObject(kind, locator, Text(item, "name"), properties.Items, properties.Complete));
            }
        }

        foreach (JsonElement drawing in drawings.Items.Where(d => Text(d, "kind") != "PropertyPlacement"))
        {
            JsonElement locator = Member(drawing, "locator");
            if (Identity(locator) is not { } key || objects.ContainsKey(key))
            {
                continue;
            }
            string kind = Text(drawing, "kind") ?? "unknown";
            objects.Add(key, new InventoryObject(kind, locator, Text(Member(drawing, "properties"), "name"), [], false));
            gaps.Add($"{kind}/{key}: drawing exists but object_find did not supply property evidence.");
        }

        ReadRows devices = await ReadAllAsync("eplan_device_list", "devices", new(args) { ["include_auxiliary"] = true }, gaps, name);
        var deviceDetails = new List<JsonElement>();
        foreach (string tag in devices.Items.Select(d => Text(d, "tag")).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            JsonElement? detail = await ReadAsync("eplan_device_get", new()
            { ["project"] = project, ["tag"] = tag, ["response_format"] = "detailed" }, gaps, name + "/" + tag);
            if (detail is not { } data)
            {
                continue;
            }
            deviceDetails.Add(data);
            if (Flag(data, "truncated"))
            {
                gaps.Add($"device_get {tag} is truncated: {Text(data, "hint")}");
            }
            // A12: a tag may have multiple main functions/representations. Only attach this evidence when
            // the returned main page and the unique main function discovered on that page agree.
            InventoryObject[] matches = objects.Values.Where(o => o.Name == tag && BooleanProperty(o, 20122) == true).ToArray();
            if (matches.Length == 1 && Text(Member(data, "device"), "page") == name)
            {
                matches[0].MainDevice = data;
            }
        }

        var references = new List<object>();
        var functions = new List<object>();
        var partReferences = new List<object>();
        var symbolCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var functionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var partCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, string[]> specialGroups = SpecialGroups();
        Dictionary<string, List<string>> specials = specialGroups.Keys.ToDictionary(k => k, _ => new List<string>(), StringComparer.Ordinal);
        foreach (InventoryObject obj in objects.Values)
        {
            string key = Identity(obj.Locator)!;
            string? category = Property(obj, 20115);
            string? nativeCategory = obj.MainDevice is { } detail ? Text(Member(detail, "device"), "category") : null;
            string? categoryName = category ?? nativeCategory;
            bool isFunction = _functionKinds.Contains(obj.Kind);
            bool? main = BooleanProperty(obj, 20122);
            if (isFunction)
            {
                functions.Add(new
                {
                    locator = obj.Locator,
                    obj.Kind,
                    obj.Name,
                    category,
                    categoryName,
                    main,
                    representation = new { page = name, pageType, functionType = Property(obj, 20121), instanceType = Property(obj, 19021) }
                });
                Increment(functionCounts, (categoryName ?? "unknown") + "/" + (main is true ? "main" : main is false ? "auxiliary" : "unknown"));
                if (category is null || main is null)
                {
                    gaps.Add($"{obj.Kind}/{key}: function category or main/auxiliary flag is unreadable.");
                }
            }

            if (isFunction || _otherSymbolKinds.Contains(obj.Kind))
            {
                string? library = Property(obj, 20111), symbol = Property(obj, 20112), variant = Property(obj, 20113);
                JsonElement? definition = null;
                if (!string.IsNullOrEmpty(library) && !string.IsNullOrEmpty(symbol))
                {
                    var cacheKey = (library, symbol);
                    if (!symbolCache.TryGetValue(cacheKey, out ReadRows? candidates))
                    {
                        var lookupGaps = new List<string>();
                        candidates = await ReadAllAsync("eplan_symbol_search", "symbols", new()
                        { ["project"] = project, ["library"] = library, ["name_contains"] = symbol }, lookupGaps, library + "/" + symbol);
                        candidates.Gaps.AddRange(lookupGaps);
                        symbolCache.Add(cacheKey, candidates);
                    }
                    gaps.AddRange(candidates.Gaps);
                    JsonElement[] exact = candidates.Items.Where(s => Text(s, "library") == library && Text(s, "name") == symbol
                        && Text(s, "variant") == variant).ToArray();
                    if (exact.Length == 1)
                    {
                        definition = exact[0];
                    }
                    else
                    {
                        gaps.Add($"{obj.Kind}/{key}: no unique symbol definition for {library}/{symbol}/{variant}.");
                    }
                }
                else
                {
                    gaps.Add($"{obj.Kind}/{key}: placed symbol library/name/variant not exposed or empty; not inferred from the main function.");
                }
                references.Add(new
                {
                    locator = obj.Locator,
                    obj.Kind,
                    obj.Name,
                    library,
                    symbol,
                    variant,
                    functionCategory = categoryName,
                    symbolFunctionCategory = definition is { } d ? Text(d, "functionCategory") : null,
                    definition
                });
                Increment(symbolCounts, JsonSerializer.Serialize(new[] { library, symbol, variant, categoryName }));
            }

            foreach ((string group, string[] terms) in specialGroups)
            {
                string label = obj.Kind + "/" + categoryName + "/" + nativeCategory;
                if (terms.Any(term => label.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    specials[group].Add(key);
                }
            }
            JsonElement[] numberedParts = Values(obj, 20100).ToArray();
            foreach (JsonElement part in numberedParts)
            {
                string? number = Text(part, "value");
                if (string.IsNullOrEmpty(number))
                {
                    continue;
                }
                string? index = Text(part, "index");
                string? quantity = Values(obj, 20102).Where(p => Text(p, "index") == index).Select(p => Text(p, "value")).FirstOrDefault();
                partReferences.Add(new { locator = obj.Locator, obj.Name, partNumber = number, index, quantity });
                Increment(partCounts, number);
            }
            if (numberedParts.Length == 0)
            {
                foreach (JsonElement part in Array(obj.MainDevice, "parts"))
                {
                    string? number = Text(part, "partNumber");
                    if (string.IsNullOrEmpty(number))
                    {
                        continue;
                    }
                    partReferences.Add(new { locator = obj.Locator, obj.Name, partNumber = number, quantity = Text(part, "count"), source = "device_get.parts" });
                    Increment(partCounts, number);
                }
            }
        }

        gaps.Add("Function representation may be absent from the type's own dictionary; available raw FUNC_TYPE/INSTANCE_TYPE and page type are retained without inventing a mapping.");
        gaps.Add("Generated-report provenance is not exposed; page type and frozen-evaluation property do not prove generation.");
        gaps.Add("Plot-frame descendants are excluded by the drawing reader; their full graphics/text inventory is unavailable.");
        gaps.Add("Circle versus arc classification is not exposed directly; Arc properties are retained without inferring a full circle.");
        gaps.Add("device_list merges identical tags; function counts use discovered object identities instead.");
        gaps.Add("Object properties use each type's own dictionary; inherited symbol fields may only be available in an unambiguous main device_get result.");
        gaps.Add("device_get omits unnamed pins and supplies no locator for auxiliary functions; its pin lists cannot prove a complete per-object pin count.");
        gaps.Add("Special groups match runtime kinds and exposed category names; localized or missing categories may remain unclassified in the complete object rows.");

        JsonElement[] placements = drawings.Items.Where(d => Text(d, "kind") == "PropertyPlacement").ToArray();
        Dictionary<string, int> kinds = objects.Values.Where(o => o.Kind != "Page").GroupBy(o => o.Kind)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        kinds["PropertyPlacement"] = placements.Length;
        Dictionary<string, int> propertiesById = placements.GroupBy(p => Text(Member(p, "properties"), "property_id") ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        string[] texts = drawings.Items.Where(d => Text(d, "kind") == "Text")
            .Select(d => Text(Member(d, "properties"), "text")).OfType<string>().Take(5).ToArray();
        int Count(string kind) => kinds.GetValueOrDefault(kind);
        TestContext.Current.TestOutputHelper!.WriteLine($"Inventory {name} ({pageType}): {objects.Count} objects, {drawings.Items.Count} drawings, {functions.Count} functions, {references.Count} symbol references, {gaps.Distinct(StringComparer.Ordinal).Count()} gaps.");
        return new
        {
            name,
            expectedType,
            page,
            pageProperties = Array(drawings.First, "properties"),
            pageDictionaryProperties = objects.Values.Where(o => o.Kind == "Page").SelectMany(o => o.Properties).ToArray(),
            form = objects.Values.Where(o => o.Kind == "Page").Select(o => Property(o, 11015)).FirstOrDefault()
                ?? Array(drawings.First, "properties").Where(p => Text(p, "id") == "11015").Select(p => Text(p, "value")).FirstOrDefault(),
            plotFrame = objects.Values.Where(o => o.Kind == "Page").Select(o => Property(o, 11016)).FirstOrDefault()
                ?? Array(drawings.First, "properties").Where(p => Text(p, "id") == "11016").Select(p => Text(p, "value")).FirstOrDefault(),
            generatedReport = (bool?)null,
            readers,
            objectCounts = kinds,
            functionCounts,
            symbolCounts,
            listedFunctionCounts = devices.Items.GroupBy(d => (Text(d, "category") ?? "unknown") + "/" + Text(d, "main"))
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            functions,
            symbolReferences = references,
            specials = specials.ToDictionary(p => p.Key, p => new
            {
                count = p.Value.Count,
                objectIds = p.Value,
                deviceRows = devices.Items.Where(d => specialGroups[p.Key].Any(term => (Text(d, "category") ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray()
            }),
            graphics = new
            {
                lines = Count("Line"),
                polylines = Count("PolyLine"),
                rectangles = Count("Rectangle"),
                arcs = Count("Arc"),
                circles = (int?)null,
                texts = Count("Text") + Count("PathText") + Count("PlaceHolderText"),
                textSamples = texts,
                images = Count("Image")
            },
            propertyPlacementsById = propertiesById,
            macros = objects.Values.Where(o => o.Kind is "MacroBox" or "MacroEntry").ToArray(),
            partNumberCounts = partCounts,
            partReferences,
            devicePartNumberCounts = objects.Values.SelectMany(o => Array(o.MainDevice, "parts"))
                .GroupBy(p => Text(p, "partNumber") ?? "unknown").ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            objects = objects.Values,
            drawings = drawings.Items,
            drawingPagesComplete = drawings.Complete,
            drawingMetadata = Member(drawings.First, "drawings"),
            deviceRows = devices.Items,
            deviceRowsComplete = devices.Complete,
            deviceDetails,
            gaps = gaps.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    private async Task<ReadRows> ReadAllAsync(string tool, string key, Dictionary<string, object?> args, List<string> gaps,
        string context, string? container = null, string cursorField = "nextCursor", string cursorArgument = "cursor",
        string limitArgument = "limit", Dictionary<string, object?>? extra = null)
    {
        var rows = new ReadRows();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var call = new Dictionary<string, object?>(args) { [limitArgument] = 200 };
            foreach ((string field, object? value) in extra ?? [])
            {
                call[field] = value;
            }
            if (cursor is not null)
            {
                call[cursorArgument] = cursor;
            }
            JsonElement? response = await ReadAsync(tool, call, gaps, context);
            if (response is not { } result)
            {
                return rows;
            }
            rows.First ??= result;
            if (Text(result, "status") == "unsupported")
            {
                gaps.Add($"{context}: {tool} unsupported: {Text(result, "reason")}");
                return rows;
            }
            JsonElement data = container is null ? result : Member(result, container);
            JsonElement items = Member(data, key);
            if (items.ValueKind != JsonValueKind.Array)
            {
                gaps.Add($"{context}: {tool} has no readable {key} array.");
                return rows;
            }
            rows.Items.AddRange(items.EnumerateArray().Select(item => item.Clone()));
            cursor = Text(data, cursorField);
            if (cursor is null && Flag(data, "truncated"))
            {
                gaps.Add($"{context}: {tool} is truncated without a continuation cursor.");
                return rows;
            }
            if (cursor is not null && !seen.Add(cursor))
            {
                gaps.Add($"{context}: {tool} repeated a continuation cursor; remaining rows are unknown.");
                return rows;
            }
        } while (cursor is not null);
        rows.Complete = true;
        return rows;
    }

    private async Task<JsonElement?> ReadAsync(string tool, Dictionary<string, object?> args, List<string> gaps, string context)
    {
        (bool succeeded, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
        if (succeeded)
        {
            return result.Clone();
        }
        gaps.Add($"{context}: {tool} read gap: {result.GetRawText()}");
        return null;
    }

    private static JsonElement Member(JsonElement? value, string name) => value is { ValueKind: JsonValueKind.Object } obj
        && obj.TryGetProperty(name, out JsonElement member) ? member : _null;
    private static string? Text(JsonElement? value, string name) => Member(value, name) is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } member
        ? member.ValueKind == JsonValueKind.String ? member.GetString() : member.GetRawText() : null;
    private static bool Flag(JsonElement? value, string name) => Member(value, name).ValueKind == JsonValueKind.True;
    private static JsonElement[] Array(JsonElement? value, string name) => Member(value, name) is { ValueKind: JsonValueKind.Array } array
        ? array.EnumerateArray().Select(item => item.Clone()).ToArray() : [];
    private static string? Identity(JsonElement locator) => Text(locator, "type") is { } type && Text(locator, "oid") is { } oid ? type + "/" + oid : null;
    private static IEnumerable<JsonElement> Values(InventoryObject obj, int id) => obj.Properties
        .Concat(Array(obj.MainDevice, "properties"))
        .Where(p => (Text(p, "property_id") ?? Text(p, "id")) == id.ToString(CultureInfo.InvariantCulture)
            && Text(p, "status") is null or "value").DistinctBy(p => Text(p, "index"));
    private static string? Property(InventoryObject obj, int id) => Values(obj, id).Select(p => Text(p, "value")).FirstOrDefault();
    private static bool? BooleanProperty(InventoryObject obj, int id) => bool.TryParse(Property(obj, id), out bool value) ? value : null;
    private static void Increment(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

    private static Dictionary<string, string[]> SpecialGroups() => new(StringComparer.Ordinal)
    {
        ["boxedDevices"] = ["BoxedDevice"],
        ["plcBoxes"] = ["PLCBox", "PLC/"],
        ["blackBoxes"] = ["BoxedDevice", "Blackbox"],
        ["locationBoxes"] = ["LocationBox"],
        ["deviceConnectionPoints"] = ["DeviceConnection", "DeviceEndTerminal"],
        ["plugsAndPins"] = ["Plug", "Pin"],
        ["potentialDefinitionPoints"] = ["PotentialDefinition", "NetDefinitionPoint"],
        ["connectionDefinitionPoints"] = ["ConnectionDefinitionPoint"],
        ["interruptionPoints"] = ["InterruptionPoint"],
        ["cables"] = ["Cable"],
        ["shields"] = ["Shield"],
        ["busbars"] = ["BusBar"],
        ["terminalsAndStrips"] = ["Terminal"],
        ["plcPoints"] = ["PLCTerminal", "PLCConnection"],
    };

    private sealed class ReadRows
    {
        public JsonElement? First { get; set; }
        public List<JsonElement> Items { get; } = [];
        public bool Complete { get; set; }
        public List<string> Gaps { get; } = [];
    }

    private sealed record InventoryObject(string Kind, JsonElement Locator, string? Name, List<JsonElement> Properties, bool PropertiesComplete)
    {
        public JsonElement? MainDevice { get; set; }
    }
}

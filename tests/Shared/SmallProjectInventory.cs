using System.Globalization;
using System.Text.Json;

namespace EplanMcp.TestSupport;

// Detached inventory orchestration shared by the live spike and its in-memory regression tests.
internal sealed class SmallProjectInventory(
    Func<string, Dictionary<string, object?>, CancellationToken, Task<(bool Succeeded, JsonElement Result)>> call,
    TimeProvider time, Action<string> log)
{
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
    internal static IReadOnlyList<(string Name, string Type)> Pages { get; } =
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

    // Only kinds absent from the drawing reader are eligible for dictionary expansion (A52-A54/A60).
    private static readonly string[] _supplementalKinds =
    [
        "Arc", "PolyLine", "Image", "PathText", "PlaceHolderText", "MacroEntry", "BusBar", "Shielding",
        "Spline", "DimensionCircle", "DimensionItem",
    ];
    private static readonly int[] _symbolPropertyIds = [20111, 20112, 20113];
    private readonly Dictionary<(string Library, string Name), ReadRows> _symbolCache = [];
    private readonly Dictionary<string, JsonElement?> _deviceCache = new(StringComparer.Ordinal);
    private long _started;
    private CancellationToken _readCancellation;
    private CancellationToken _budgetCancellation;
    private CancellationToken _externalCancellation;
    private bool BudgetExpired => time.GetElapsedTime(_started) >= TimeSpan.FromMinutes(25);

    internal async Task RunAsync(string project, Func<IReadOnlyList<object>, Task> saveAsync, CancellationToken cancellationToken = default)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(25), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);
        _readCancellation = linked.Token;
        _budgetCancellation = budget.Token;
        _externalCancellation = cancellationToken;
        _symbolCache.Clear();
        _deviceCache.Clear();
        _started = time.GetTimestamp();
        var pages = new List<object>();
        foreach ((string name, string expectedType) in Pages)
        {
            long started = time.GetTimestamp();
            var gaps = new List<string>();
            pages.Add(await InventoryPageAsync(project, name, expectedType, gaps));
            await saveAsync(pages);
            log(FormattableString.Invariant($"Inventory {name}: {time.GetElapsedTime(started).TotalSeconds:F1} seconds."));
            if (BudgetExpired)
            {
                if (!gaps.Any(g => g.Contains("25-minute", StringComparison.Ordinal)))
                {
                    gaps.Add("25-minute run budget reached while saving; remaining pages were not started.");
                    await saveAsync(pages);
                }
                break;
            }
        }
    }

    private async Task<object> InventoryPageAsync(string project, string name, string expectedType, List<string> gaps)
    {
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
            gaps.Add("page_get reported truncation; drawing continuations are followed, but omitted page property rows may remain unavailable.");
        }

        var objects = new Dictionary<string, InventoryObject>(StringComparer.Ordinal);
        var readers = new Dictionary<string, object>(StringComparer.Ordinal);
        // A52-A54: the drawing rows already identify all placed symbols and their property texts.
        foreach (JsonElement drawing in drawings.Items.Where(d => Text(d, "kind") != "PropertyPlacement"))
        {
            JsonElement locator = Member(drawing, "locator");
            if (Identity(locator) is not { } key)
            {
                gaps.Add("A drawing row has no readable locator; retained in drawings.");
                continue;
            }
            string kind = Text(drawing, "kind") ?? "unknown";
            JsonElement properties = Member(drawing, "properties");
            objects.TryAdd(key, new InventoryObject(kind, locator, Text(properties, "name"), [], false)
            {
                Category = Text(properties, "function_category") ?? Text(properties, "category"),
                Main = bool.TryParse(Text(properties, "main"), out bool main) ? main : null,
            });
        }

        if (pageType is "Circuit" or "Overview")
        {
            foreach (string kind in _supplementalKinds.Where(k => !objects.Values.Any(o => o.Kind == k)))
            {
                ReadRows found = await ReadAllAsync("eplan_object_find", "items",
                    new(args) { ["object_type"] = kind }, gaps, name + "/" + kind, cursorField: "cursor", maxItems: 200);
                readers[kind] = new { status = found.Complete ? "complete" : "partial", count = found.Items.Count };
                foreach (JsonElement item in found.Items)
                {
                    JsonElement locator = Member(item, "locator");
                    if (Identity(locator) is not { } key || objects.ContainsKey(key))
                    {
                        continue;
                    }
                    ReadRows properties = await ReadAllAsync("eplan_object_get", "items",
                        new() { ["project"] = project, ["object_type"] = kind, ["locator"] = locator, ["response_format"] = "detailed" },
                        gaps, name + "/" + kind + "/" + key, cursorField: "cursor");
                    objects.Add(key, new InventoryObject(kind, locator, Text(item, "name"), properties.Items, properties.Complete));
                }
            }
        }

        ReadRows devices = await ReadAllAsync("eplan_device_list", "devices", new(args) { ["include_auxiliary"] = true }, gaps, name);
        foreach (IGrouping<string, InventoryObject> group in objects.Values.Where(o => _functionKinds.Contains(o.Kind) && o.Name is not null)
            .GroupBy(o => o.Name!, StringComparer.Ordinal))
        {
            JsonElement[] listed = devices.Items.Where(d => Text(d, "tag") == group.Key).ToArray();
            if (group.Count() == 1 && listed.Length == 1)
            {
                group.Single().Category ??= Text(listed[0], "category");
                group.Single().Main ??= Member(listed[0], "main").ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? Flag(listed[0], "main") : null;
            }
        }

        var deviceDetails = new List<JsonElement>();
        string[] tags = devices.Items.Where(d => Flag(d, "main")).Select(d => Text(d, "tag")).OfType<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        if (tags.Length > 30)
        {
            gaps.Add($"device_get sampled 30 of {tags.Length} distinct main devices; remaining part numbers/templates were not read.");
        }
        foreach (string tag in tags.Take(30))
        {
            if (!_deviceCache.TryGetValue(tag, out JsonElement? detail))
            {
                detail = await ReadAsync("eplan_device_get", new()
                { ["project"] = project, ["tag"] = tag, ["response_format"] = "detailed" }, gaps, name + "/" + tag);
                _deviceCache.Add(tag, detail);
            }
            if (detail is not { } data)
            {
                gaps.Add($"device_get {tag}: no readable cached part/template data.");
                continue;
            }
            deviceDetails.Add(data);
            if (Flag(data, "truncated"))
            {
                gaps.Add($"device_get {tag} is truncated: {Text(data, "hint")}");
            }
            // A12: a tag may have multiple main functions/representations. Attach only unambiguous evidence.
            InventoryObject[] matches = objects.Values.Where(o => o.Name == tag && o.Main == true).ToArray();
            if (matches.Length == 1 && Text(Member(data, "device"), "page") == name)
            {
                matches[0].MainDevice = data;
            }
        }

        await ReadSymbolPropertiesAsync(project, objects.Values, gaps, name);

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
            string? category = obj.Category ?? Property(obj, 20115);
            string? nativeCategory = obj.MainDevice is { } detail ? Text(Member(detail, "device"), "category") : null;
            string? categoryName = category ?? nativeCategory;
            bool isFunction = _functionKinds.Contains(obj.Kind);
            bool? main = obj.Main ?? BooleanProperty(obj, 20122);
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
                    if (!_symbolCache.TryGetValue(cacheKey, out ReadRows? candidates))
                    {
                        var lookupGaps = new List<string>();
                        candidates = await ReadAllAsync("eplan_symbol_search", "symbols", new()
                        { ["project"] = project, ["library"] = library, ["name_contains"] = symbol }, lookupGaps, library + "/" + symbol);
                        candidates.Gaps.AddRange(lookupGaps);
                        _symbolCache.Add(cacheKey, candidates);
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

        // Keep sampled part evidence even when tag merging prevents attaching it to one placed function.
        HashSet<string> attachedTags = objects.Values.Where(o => o.MainDevice is not null && o.Name is not null)
            .Select(o => o.Name!).ToHashSet(StringComparer.Ordinal);
        foreach (JsonElement detail in deviceDetails.Where(d => !attachedTags.Contains(Text(Member(d, "device"), "tag") ?? "")))
        {
            foreach (JsonElement part in Array(detail, "parts"))
            {
                if (Text(part, "partNumber") is not { Length: > 0 } number)
                {
                    continue;
                }
                partReferences.Add(new
                {
                    locator = (object?)null,
                    Name = Text(Member(detail, "device"), "tag"),
                    partNumber = number,
                    quantity = Text(part, "count"),
                    source = "device_get.parts",
                });
                Increment(partCounts, number);
            }
        }

        gaps.Add("Function representation may be absent from the type's own dictionary; available raw FUNC_TYPE/INSTANCE_TYPE and page type are retained without inventing a mapping.");
        gaps.Add("Generated-report provenance is not exposed; page type and frozen-evaluation property do not prove generation.");
        gaps.Add("Plot-frame descendants are excluded by the drawing reader; their full graphics/text inventory is unavailable.");
        gaps.Add("Circle versus arc classification is not exposed directly; Arc properties are retained without inferring a full circle.");
        gaps.Add("device_list merges identical tags; function counts use discovered object identities instead.");
        gaps.Add("device_list merges identical tags; category/main flags are attached only to an unambiguous drawing object. Other representations remain unknown.");
        gaps.Add("device_get omits unnamed pins and supplies no locator for auxiliary functions; its pin lists cannot prove a complete per-object pin count.");
        gaps.Add("Special groups match runtime kinds and exposed category names; localized or missing categories may remain unclassified in the retained object rows.");
        if (pageType is not ("Circuit" or "Overview"))
        {
            gaps.Add("Supplemental dictionary kinds are only probed on Circuit and Overview pages; this page uses drawing rows only.");
        }

        JsonElement[] placements = drawings.Items.Where(d => Text(d, "kind") == "PropertyPlacement").ToArray();
        Dictionary<string, int> kinds = objects.Values.Where(o => o.Kind != "Page").GroupBy(o => o.Kind)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        kinds["PropertyPlacement"] = placements.Length;
        Dictionary<string, int> propertiesById = placements.GroupBy(p => Text(Member(p, "properties"), "property_id") ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        string[] texts = drawings.Items.Where(d => Text(d, "kind") == "Text")
            .Select(d => Text(Member(d, "properties"), "text")).OfType<string>().Take(5).ToArray();
        int Count(string kind) => kinds.GetValueOrDefault(kind);
        log($"Page {name} ({pageType}): {objects.Count} objects, {drawings.Items.Count} drawings, {functions.Count} functions, {references.Count} symbol references, {gaps.Distinct(StringComparer.Ordinal).Count()} gaps.");
        if (BudgetExpired)
        {
            gaps.Add("25-minute run budget reached; remaining reads/pages were not started. Completed and partial page evidence is retained.");
        }
        string[] uniqueGaps = gaps.Distinct(StringComparer.Ordinal).ToArray();
        gaps.Clear();
        gaps.AddRange(uniqueGaps);
        return new
        {
            name,
            expectedType,
            page,
            pageProperties = Array(drawings.First, "properties"),
            pageDictionaryProperties = System.Array.Empty<JsonElement>(),
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
            devicePartNumberCounts = deviceDetails.SelectMany(d => Array(d, "parts"))
                .GroupBy(p => Text(p, "partNumber") ?? "unknown").ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            objects = objects.Values,
            drawings = drawings.Items,
            drawingPagesComplete = drawings.Complete,
            drawingMetadata = Member(drawings.First, "drawings"),
            deviceRows = devices.Items,
            deviceRowsComplete = devices.Complete,
            deviceDetails,
            gaps,
        };
    }

    private async Task ReadSymbolPropertiesAsync(string project, IEnumerable<InventoryObject> objects, List<string> gaps, string page)
    {
        // A44/A60: function locators accept derived functions; drawing digests are not property target fields.
        // Other SymbolReference kinds have no selective property target in the published read contract.
        InventoryObject[] symbols = objects.Where(o => _functionKinds.Contains(o.Kind)).ToArray();
        foreach (InventoryObject obj in objects.Where(o => _otherSymbolKinds.Contains(o.Kind)))
        {
            gaps.Add($"{obj.Kind}/{Identity(obj.Locator)}: the selective property reader has no target for this kind; symbol identity remains unavailable.");
        }
        foreach (InventoryObject[] batch in symbols.Chunk(50))
        {
            object[] queries = batch.Select(obj => (object)new
            {
                target = new
                {
                    locator = new
                    {
                        project,
                        kind = "function",
                        type = Member(obj.Locator, "type"),
                        oid = Member(obj.Locator, "oid"),
                        page,
                    }
                },
                properties = _symbolPropertyIds.Select(id => new { property_id = id }).ToArray(),
            }).ToArray();
            ReadRows properties = await ReadAllAsync("eplan_property_get", "items", new()
            {
                ["project"] = project,
                ["queries"] = queries,
                ["response_format"] = "detailed",
            }, gaps, page + "/symbol identities", cursorField: "cursor");
            foreach (JsonElement row in properties.Items)
            {
                if (Member(row, "query") is not { ValueKind: JsonValueKind.Number } query
                    || !query.TryGetInt32(out int index) || index < 0 || index >= batch.Length)
                {
                    gaps.Add($"{page}: symbol property row has no valid query index.");
                    continue;
                }
                batch[index].Properties.Add(row);
                if (Text(row, "status") is "read_refused" or "not_applicable")
                {
                    gaps.Add($"{batch[index].Kind}/{Identity(batch[index].Locator)}: symbol property {Text(row, "property_id")} is {Text(row, "status")}: {Text(row, "reason")}.");
                }
            }
        }
    }

    private async Task<ReadRows> ReadAllAsync(string tool, string key, Dictionary<string, object?> args, List<string> gaps,
        string context, string? container = null, string cursorField = "nextCursor", string cursorArgument = "cursor",
        string limitArgument = "limit", Dictionary<string, object?>? extra = null, int? maxItems = null)
    {
        var rows = new ReadRows();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var arguments = new Dictionary<string, object?>(args) { [limitArgument] = maxItems is { } cap ? Math.Min(200, cap - rows.Items.Count) : 200 };
            foreach ((string field, object? value) in extra ?? [])
            {
                arguments[field] = value;
            }
            if (cursor is not null)
            {
                arguments[cursorArgument] = cursor;
            }
            JsonElement? response = await ReadAsync(tool, arguments, gaps, context);
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
            rows.Items.AddRange(items.EnumerateArray().Take(maxItems is { } maximum ? maximum - rows.Items.Count : int.MaxValue).Select(item => item.Clone()));
            cursor = Text(data, cursorField);
            if (cursor is null && Flag(data, "truncated"))
            {
                gaps.Add($"{context}: {tool} is truncated without a continuation cursor.");
                return rows;
            }
            if (maxItems is { } limit && rows.Items.Count >= limit && cursor is not null)
            {
                gaps.Add($"{context}: {tool} capped at {limit} objects; remaining objects were not read.");
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
        if (BudgetExpired)
        {
            gaps.Add($"{context}: 25-minute run budget reached before {tool}; remaining reads were not started.");
            return null;
        }
        (bool succeeded, JsonElement result) response;
        try
        {
            response = await call(tool, args, _readCancellation);
        }
        catch (OperationCanceledException) when (_budgetCancellation.IsCancellationRequested && !_externalCancellation.IsCancellationRequested)
        {
            gaps.Add($"{context}: 25-minute run budget cancelled {tool}; remaining reads were not started.");
            return null;
        }
        (bool succeeded, JsonElement result) = response;
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
        public string? Category { get; set; }
        public bool? Main { get; set; }
    }
}

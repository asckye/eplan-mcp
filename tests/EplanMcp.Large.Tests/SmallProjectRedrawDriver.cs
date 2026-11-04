using System.Globalization;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

/// <summary>Replays detached sample-page evidence exclusively through MCP tools, retaining every failure.</summary>
internal sealed partial class SmallProjectRedrawDriver(
    Func<string, JObject, Task<JObject>> call, string project, string importsRoot, SmallProjectRedrawReport report)
{
    internal static readonly (double X, double Y) OverviewMacroPosition = (0, 292);
    private readonly Dictionary<string, string> _actualNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _placedTags = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attemptedPages = new(StringComparer.Ordinal);
    private readonly HashSet<string> _createdPages = new(StringComparer.Ordinal);
    private readonly HashSet<string> _covered = new(StringComparer.Ordinal);
    private bool _lastDrawingReadComplete;

    internal async Task DrawPageAsync(JObject source, IReadOnlyList<JObject> allPages)
    {
        string page = Text(source["name"]) ?? throw new InvalidDataException("Export has no page name.");
        JObject[] objects = Objects(source["placements"]);
        if (IsReportPage(source))
        {
            await DrawReportsAsync(page, source);
            return;
        }

        await CreatePagesAsync(allPages);
        if (!_createdPages.Contains(page))
        {
            report.Warning(page, "Page creation did not succeed; no drawing calls were sent for this page.");
            return;
        }
        // Opening the page is an MCP read/UI operation and makes it observable during a live run.
        await InvokeAsync(page, "eplan_page_open", new JObject { ["page"] = page }, [source], write: false);

        foreach (JObject block in objects.Where(o => Type(o) == "Block"))
        {
            report.Note(page, "Block", Id(block), "Block membership is retained for macro coverage and comparison: "
                + (block["memberOids"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "unreadable") + ". No block-creation MCP route exists.");
            report.NoTool(page, "Block", Id(block), "No MCP tool creates native Block membership.");
        }
        if (Text(source["pageType"]) == "Overview")
        { await InsertOverviewMacrosAsync(page, objects); }

        foreach (JObject item in objects.Where(IsDevice).OrderBy(DeviceOrder))
        {
            if (!_covered.Contains(Key(page, item)))
            { await ObserveAsync(page, item, () => PlaceDeviceAsync(page, item)); }
        }
        foreach (JObject item in objects.Where(o => Type(o) == "LocationBox"))
        {
            if (!_covered.Contains(Key(page, item)))
            { await ObserveAsync(page, item, () => PlaceLocationBoxAsync(page, item)); }
        }
        foreach (JObject item in objects.Where(o => Type(o) is "SymbolReference" or "InterruptionPoint"))
        {
            if (!_covered.Contains(Key(page, item)))
            { await ObserveAsync(page, item, () => PlaceNodeAsync(page, item, allPages)); }
        }

        await InvokeAsync(page, "eplan_connection_generate", new JObject { ["pages"] = new JArray(page) },
            objects.Where(o => IsDevice(o) || Type(o) is "SymbolReference" or "InterruptionPoint").ToArray());
        await PlaceDefinitionsAsync(page, objects);
        foreach (JObject item in objects.Where(o => Type(o) == "Cable" && !_covered.Contains(Key(page, o))))
        { await ObserveAsync(page, item, () => PlaceCableAsync(page, item, source)); }
        foreach (JObject item in objects.Where(o => Type(o) == "PotentialDefinition"))
        { report.NoTool(page, Type(item), Id(item), "A86: no MCP tool creates potential definitions."); }
        await DrawGraphicsAsync(page, objects);
        await MovePropertiesAsync(page, objects);
        await ReviewAsync(page, source);
        foreach (JObject item in objects.Where(o => !IsDevice(o) && Type(o) is not ("LocationBox" or "SymbolReference" or "InterruptionPoint"
            or "ConnectionDefinitionPoint" or "Cable" or "PotentialDefinition" or "Block" or "ContextPropertyPlacement" or "PropertyPlacement") && !IsGraphic(o)))
        { report.NoTool(page, Type(item), Id(item), "No documented redraw mapping for this exported object type."); }
    }

    internal async Task CreatePagesAsync(IReadOnlyList<JObject> sources)
    {
        foreach (JObject source in sources.Where(source => !IsReportPage(source)))
        {
            string page = Text(source["name"]) ?? throw new InvalidDataException("Export has no page name.");
            if (!_attemptedPages.Add(page))
            { continue; }
            var item = new JObject { ["name"] = page, ["page_type"] = source["pageType"]?.DeepClone() };
            if (Multilingual(source["description"]) is string description)
            { item["description"] = description; }
            if (await InvokeAsync(page, "eplan_page_create", new JObject { ["pages"] = new JArray(item) }, [source]) is not null)
            { _createdPages.Add(page); }
        }
    }

    private static bool IsReportPage(JObject source) => (bool?)source["isReportPage"] == true
        || Objects(source["report"]).Length > 0 || Text(source["pageType"]) is not ("Circuit" or "Overview");
    private async Task DrawReportsAsync(string page, JObject source)
    {
        JObject[] blocks = Objects(source["report"]);
        if (blocks.Length == 0)
        {
            report.NoTool(page, Text(source["pageType"]) ?? "Page", "", "No report block was exported; eplan_page_create does not support this report page type.");
            return;
        }
        foreach (JObject block in blocks)
        {
            var args = new JObject { ["type"] = block["type"]?.DeepClone(), ["form"] = block["formName"]?.DeepClone(), ["first_page"] = page };
            foreach ((string from, string to) in new[]
            {
                ("filterSchemaName", "filter_scheme"), ("sortSchemaName", "sort_scheme"),
                ("filterSchemaData", "filter_scheme_data"), ("sortSchemaData", "sort_scheme_data"),
                ("functionFilterSchemaData", "function_filter_scheme_data"), ("functionSortSchemaData", "function_sort_scheme_data"),
            })
            {
                if (!string.IsNullOrEmpty(Text(block[from])))
                { args[to] = block[from]!.DeepClone(); }
            }
            if ((bool?)block["isAutomaticPageDescription"] == false && Multilingual(block["manualPageDescription"]) is string manual)
            { args["page_description"] = manual; }
            if (!string.IsNullOrEmpty(Text(block["deviceTag"])))
            { report.Note(page, "ReportBlock", "", "Source deviceTag is comparison evidence only; report_create does not support device restrictions."); }
            foreach (string field in new[] { "functionFilterSchemaName", "functionSortSchemaName" })
            {
                if (!string.IsNullOrEmpty(Text(block[field])))
                { report.Note(page, "ReportBlock", "", field + " has no report_create parameter; corresponding raw scheme data is passed unchanged."); }
            }
            await InvokeAsync(page, "eplan_report_create", args, [new JObject { ["type"] = "ReportBlock" }]);
        }
    }

    private async Task InsertOverviewMacrosAsync(string page, JObject[] objects)
    {
        foreach (JObject box in objects.Where(o => Text(o["category"]) == "PLCBox" || Type(o) == "PLC"))
        {
            JObject? part = Objects(box["parts"]).FirstOrDefault();
            if (part is null)
            {
                report.Note(page, Type(box), Id(box), "PLC box has no exported part reference; using individual objects.");
                continue;
            }
            await ObserveAsync(page, box, async () =>
            {
                int variant = (int?)box["symbol"]?["variant"] ?? 0;
                report.Note(page, Type(box), Id(box), FormattableString.Invariant(
                    $"Overview macro position: variant={variant}; top-left=({OverviewMacroPosition.X},{OverviewMacroPosition.Y}). Coverage requires fresh drawing evidence."));
                var before = (await ReadDrawingsAsync(page, box)).Select(DrawingKey).ToHashSet(StringComparer.Ordinal);
                bool beforeComplete = _lastDrawingReadComplete;
                JObject? result = await InvokeAsync(page, "eplan_macro_insert", new JObject
                {
                    ["page"] = page,
                    ["part"] = part["part"]?.DeepClone(),
                    ["part_variant"] = part["variant"]?.DeepClone(),
                    ["representation"] = "Overview",
                    ["variant"] = variant,
                    ["x"] = OverviewMacroPosition.X,
                    ["y"] = OverviewMacroPosition.Y,
                }, [box]);
                if (result is null)
                {
                    report.Note(page, Type(box), Id(box), "Overview macro insertion failed (including absent Overview macro); continuing with individual objects.");
                    return;
                }
                // Rename only the unambiguous PLC box returned by this insertion. Other macro tags remain visible differences.
                JObject[] cards = Objects(result["devices"]).Where(d => Text(d["category"]) == "PLCBox").ToArray();
                if ((bool?)result["recovered"] != true && cards.Length == 1 && Text(cards[0]["tag"]) is string inserted && Text(box["name"]) is string wanted)
                {
                    if (inserted != wanted)
                    { await InvokeAsync(page, "eplan_device_update", new JObject { ["tag"] = inserted, ["new_tag"] = wanted }, [box]); }
                }
                JObject[] actual = await ReadDrawingsAsync(page, box);
                JObject[] insertedBoxes = actual.Where(a => !before.Contains(DrawingKey(a)) && Text(a["kind"]) == Type(box)
                    && (Text(a["properties"]?["name"]) == Text(box["name"])
                        || cards.Any(card => Text(card["tag"]) == Text(a["properties"]?["name"])))).ToArray();
                if (insertedBoxes.Length == 1 && TryPoint(box["location"], out double[] sourcePoint)
                    && TryPoint(insertedBoxes[0]["location"], out double[] targetPoint))
                {
                    _covered.Add(Key(page, box));
                    report.Note(page, Type(box), Id(box), FormattableString.Invariant(
                        $"Overview PLC box position: source=({sourcePoint[0]},{sourcePoint[1]}); target=({targetPoint[0]},{targetPoint[1]}); dx={targetPoint[0] - sourcePoint[0]}; dy={targetPoint[1] - sourcePoint[1]} (mm)."));
                }
                else
                { report.Warning(page, "Overview PLC box position could not be compared: target box is absent, ambiguous, or has unreadable coordinates."); }
                HashSet<string> members = MacroMemberIds(box, objects);
                foreach (JObject point in objects.Where(o => members.Contains(Id(o)) && Text(o["category"]) == "PLCTerminal"))
                {
                    _covered.Add(Key(page, point));
                    report.Note(page, Type(point), Id(point), "PLC point supplied by the successful Overview macro; individual placement omitted.");
                }
                if (Text(box["name"]) is string boxTag)
                { _placedTags.Add(boxTag); }
                if (!beforeComplete || !_lastDrawingReadComplete)
                {
                    report.Warning(page, "Macro coverage could not be proved from complete before/after MCP drawing reads; objects remain eligible for individual redraw.");
                    return;
                }
                var available = actual.Where(a => a["parent"]?.Type is null or JTokenType.Null && !before.Contains(DrawingKey(a))).ToList();

                foreach (JObject candidate in objects.Where(o => members.Contains(Id(o)) && Type(o) != "Block" && Type(o) is not ("PropertyPlacement" or "ContextPropertyPlacement")))
                {
                    JObject? match = available.FirstOrDefault(a => DrawingMatches(candidate, a));
                    if (match is null)
                    { continue; }
                    _covered.Add(Key(page, candidate));
                    available.Remove(match);
                    report.Note(page, Type(candidate), Id(candidate), "Covered by the inserted Overview macro; native type, position and available name match fresh MCP drawing evidence.");
                }
            });
        }
    }

    private async Task PlaceDeviceAsync(string page, JObject item)
    {
        JObject symbol = item["symbol"] as JObject ?? throw new InvalidDataException("Exported symbol is unreadable.");
        double[] location = Point(item["location"]);
        var args = new JObject
        {
            ["page"] = page,
            ["library"] = symbol["library"]?.DeepClone(),
            ["symbol"] = symbol["name"]?.DeepClone(),
            ["symbol_variant"] = symbol["variant"]?.DeepClone(),
            ["x"] = location[0],
            ["y"] = location[1],
        };
        string name = Text(item["name"]) ?? throw new InvalidDataException("Exported function name is unreadable.");
        string symbolName = Text(symbol["name"]) ?? "";
        string[] pins = Objects(item["connectionDesignations"]).OrderBy(p => (int?)p["index"] ?? 0).Select(p => Text(p["value"]) ?? "").ToArray();
        if (pins.Length == 0)
        { pins = Objects(item["pins"]).OrderBy(p => (int?)p["index"] ?? 0).Select(p => Text(p["name"]) ?? "").ToArray(); }
        if (Type(item) == "Terminal" && Text(item["category"]) != "PLCTerminal")
        {
            int colon = name.LastIndexOf(':');
            if (colon < 0)
            { throw new InvalidDataException("Terminal strip/number cannot be read from the exported native name."); }
            args["terminal_strip"] = name[..colon];
            args["terminal_number"] = name[(colon + 1)..];
        }
        else
        {
            string tag = symbolName is "DCP" or "DCPP3" || Text(item["category"]) == "PLCTerminal"
                ? Text(item["parentFunction"]?["name"]) ?? DeviceName(name) : name;
            args["tag"] = _actualNames.GetValueOrDefault(tag, tag);
            if (pins.Length > 0)
            { args["connection_designations"] = new JArray(pins); }
            if (symbolName == "DC")
            {
                double[] area = LogicalBounds(item);
                args["x"] = area[0];
                args["y"] = area[1];
                args["corner_x"] = area[2];
                args["corner_y"] = area[3];
            }
            if (Text(item["category"]) == "PLCTerminal")
            {
                string? point = pins.FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? Property(item, 20022);
                if (!string.IsNullOrEmpty(point))
                { args["plc_connection_point"] = point; }
                foreach ((int id, string field) in new[] { (20400, "plc_address"), (20402, "plc_symbolic_address"), (20011, "plc_text"), (20406, "plc_plug_designation") })
                {
                    if (Property(item, id) is string value && !string.IsNullOrEmpty(value))
                    { args[field] = value; }
                }
            }
        }
        string placedTag = Text(args["tag"]) ?? name;
        if (_placedTags.Contains(placedTag))
        { args["join_device"] = true; }
        JObject? placed = await InvokeAsync(page, "eplan_device_place", args, [item]);
        if (placed is null)
        { return; }
        _placedTags.Add(placedTag);
        if (Text(placed["device"]?["tag"]) is string actualTag)
        {
            _actualNames[name] = actualTag;
            _placedTags.Add(actualTag);
        }
        if ((bool?)item["isMain"] == true)
        {
            foreach (JObject part in Objects(item["parts"]))
            {
                await InvokeAsync(page, "eplan_device_update", new JObject
                {
                    ["tag"] = _actualNames.GetValueOrDefault(name, name),
                    ["part"] = part["part"]?.DeepClone(),
                    ["variant"] = part["variant"]?.DeepClone(),
                }, [item]);
                if ((double?)part["count"] is double count && count != 1)
                { report.Note(page, Type(item), Id(item), "Part reference quantity " + count.ToString(CultureInfo.InvariantCulture) + " cannot be specified by device_update."); }
            }
        }
    }

    private async Task PlaceLocationBoxAsync(string page, JObject item)
    {
        double[] area = LogicalBounds(item);
        await InvokeAsync(page, "eplan_device_place", new JObject
        {
            ["page"] = page,
            ["location_box"] = item["structure"]?.DeepClone(),
            ["x"] = area[0],
            ["y"] = area[1],
            ["corner_x"] = area[2],
            ["corner_y"] = area[3],
        }, [item]);
    }

    private async Task PlaceNodeAsync(string page, JObject item, IReadOnlyList<JObject> allPages)
    {
        var node = new JObject();
        if (Type(item) == "InterruptionPoint")
        {
            string name = Text(item["name"]) ?? throw new InvalidDataException("Interruption point name is unreadable.");
            string? reference = Text(item["counterpartReference"]);
            string[] counterparts = allPages.Where(p => Text(p["name"]) != page && Text(p["pageType"]) == "Circuit"
                && Objects(p["placements"]).Any(o => (Type(o) == "InterruptionPoint" && Text(o["name"]) == name)
                    || (IsDevice(o) && Text(o["name"]) is string device && reference is not null
                        && (reference == device || reference.StartsWith(device + ":", StringComparison.Ordinal)))))
                .Select(p => Text(p["name"])!).Distinct(StringComparer.Ordinal).ToArray();
            if (counterparts.Length is 0 or > 20)
            {
                report.Note(page, Type(item), Id(item), "Interruption point not placed: no supported set of 1..20 counterpart Circuit pages from exported same-name points or counterpartReference=" + reference + ".");
                return;
            }
            JObject? pin = Objects(item["connectionPoints"]).FirstOrDefault(p => JToken.DeepEquals(p["index"], item["connectionPointIndex"]));
            node = new JObject
            {
                ["kind"] = "interruption_point",
                ["name"] = name,
                ["symbol"] = item["symbol"]?["name"]?.DeepClone(),
                ["direction"] = item["direction"]?.DeepClone(),
                ["position"] = new JArray(Point(pin?["position"] ?? item["location"])),
                ["counterpart_pages"] = new JArray(counterparts),
            };
            if (Property(item, 24809) is string order && int.TryParse(order, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sortCode))
            { node["sort_code"] = sortCode; }
        }
        else
        {
            string[] directions = Objects(item["connectionPoints"]).Select(p => Text(p["direction"]))
                .Where(d => d is "Up" or "Down" or "Left" or "Right").Select(d => d!).Distinct(StringComparer.Ordinal).ToArray();
            string kind = directions.Length switch { 1 => "potential_point", 2 => "corner", 3 => "t_node", _ => throw new InvalidDataException("Node has no supported one/two/three-direction connection-point evidence.") };
            node = new JObject { ["kind"] = kind, ["position"] = new JArray(Point(item["location"])), ["directions"] = new JArray(directions) };
            if (kind == "t_node")
            { node["variant"] = item["symbol"]?["variant"]?.DeepClone(); }
            else if (item["symbol"]?["variant"] is not null)
            { report.Note(page, Type(item), Id(item), "The node route selects its variant from directions; explicit variant is only accepted for t_node."); }
        }
        await InvokeAsync(page, "eplan_connection_create", new JObject { ["page"] = page, ["snap_to_grid"] = false, ["items"] = new JArray(node) }, [item]);
    }

    private async Task PlaceDefinitionsAsync(string page, JObject[] objects)
    {
        var mapped = new List<(JObject Source, JObject Point)>();
        foreach (JObject item in objects.Where(o => Type(o) == "ConnectionDefinitionPoint" && !_covered.Contains(Key(page, o))))
        {
            await ObserveAsync(page, item, () =>
            {
                var point = new JObject
                {
                    ["kind"] = "definition_point",
                    ["position"] = new JArray(Point(item["location"])),
                    ["symbol"] = item["symbol"]?["name"]?.DeepClone(),
                    ["symbol_variant"] = item["symbol"]?["variant"]?.DeepClone(),
                };
                foreach ((int id, string field) in new[] { (31004, "color"), (31002, "cross_section"), (31011, "designation") })
                {
                    if (Property(item, id) is string value && !string.IsNullOrEmpty(value))
                    { point[field] = value; }
                }
                if (point["color"] is null && point["cross_section"] is null && point["designation"] is null)
                {
                    report.Note(page, Type(item), Id(item), "Definition point not placed: exported part references alone cannot satisfy the required color, cross_section or designation property.");
                    return Task.CompletedTask;
                }
                mapped.Add((item, point));
                return Task.CompletedTask;
            });
        }
        foreach ((JObject Source, JObject Point)[] batch in mapped.Chunk(50))
        {
            await InvokeItemsAsync(page, "eplan_connection_create", new JObject
            {
                ["page"] = page,
                ["snap_to_grid"] = false,
                ["items"] = new JArray(batch.Select(item => item.Point)),
            }, batch.Select(item => item.Source).ToArray());
        }
    }
    private Task PlaceCableAsync(string page, JObject item, JObject source)
    {
        // The export has a native symbol and its ink bounds, not CABDL's definition-line endpoints or core ownership.
        // Those cannot be inferred from crossing graphics or a bounding box without inventing connectivity.
        report.Note(page, "Cable", Id(item), "cable_definition mapping incomplete: exported Cable lacks definition-line endpoints and verified core-to-connection ownership. "
            + "Available name=" + Text(item["name"]) + "; source connections=" + Objects(source["connections"]).Length.ToString(CultureInfo.InvariantCulture) + ". No native fallback was used.");
        return Task.CompletedTask;
    }

    private async Task ObserveAsync(string page, JObject item, Func<Task> action)
    {
        try
        { await action(); }
        catch (OperationCanceledException) when (TestContext.Current.CancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { report.Warning(page, Type(item) + " " + Id(item) + ": mapping unavailable: " + error.Message); }
    }

    private static bool IsDevice(JObject item) => Type(item) is "Function" or "Terminal" or "PLC" or "BoxedDevice" or "BusTerminal";
    private static int DeviceOrder(JObject item) => Text(item["symbol"]?["name"]) switch { "DC" => 0, "BBDEF" => 1, "PLCC" => 2, _ => 3 };
    private static string DeviceName(string name) => name.Contains(':', StringComparison.Ordinal) ? name[..name.IndexOf(':', StringComparison.Ordinal)] : name;
    private static JObject[] Objects(JToken? token) => token is JArray array ? array.OfType<JObject>().ToArray() : [];
    private static string Type(JObject item) => Text(item["type"]) ?? "Page";
    private static string Id(JObject item) => item["oid"]?.ToString() ?? "";
    private static string Key(string page, JObject item) => page + "\n" + Id(item);
    private static string? Text(JToken? token) => token?.Type == JTokenType.String ? (string?)token : null;
    private static string? Multilingual(JToken? token) => Text(token) ?? (token is JObject obj
        ? Text(obj["??_??"]) ?? Text(obj["en_US"]) ?? obj.Properties().Select(p => Text(p.Value)).FirstOrDefault(v => !string.IsNullOrEmpty(v)) : null);
    private static string? Property(JObject item, int id) => Objects(item["properties"]).Where(p => (int?)p["id"] == id)
        .OrderBy(p => (int?)p["index"] ?? 0).Select(p => Multilingual(p["value"])).FirstOrDefault(v => v is not null);
    private static double[] Point(JToken? token) => token is JObject obj && Number(obj["x"]) is double x && Number(obj["y"]) is double y ? [x, y]
        : token is JArray array && array.Count == 2 && Number(array[0]) is double ax && Number(array[1]) is double ay ? [ax, ay]
        : throw new InvalidDataException("Point coordinates are absent or unreadable.");
    private static double? Number(JToken? token) => token?.Type is JTokenType.Integer or JTokenType.Float && double.IsFinite((double)token) ? (double)token : null;
    private static double[] LogicalBounds(JObject item) => item["logicalArea"] is JObject area
        ? [.. Point(area["lowerLeft"]), .. Point(area["upperRight"])] : throw new InvalidDataException("Native logical area is absent or unreadable; ink bounds do not substitute for a box's area.");
}

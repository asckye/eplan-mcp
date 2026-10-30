using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

internal sealed class AcceptanceRedrawDriver(
    Func<string, Dictionary<string, object?>, Task<(bool Succeeded, JsonElement Result)>> call,
    string project, JsonElement list, string run, Action<string> output, string? next = null)
{
    private readonly Dictionary<string, JsonElement> _placed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _pins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ResolvedPin From, ResolvedPin To, int? ItemIndex)> _wires = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _actualTags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _devicePages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Tag, string Pin)> _interruptionSupport = new(StringComparer.Ordinal);
    private readonly List<object> _evidence = [];
    private readonly List<object> _skipped = [];
    private readonly List<string> _stages = [];
    private double _tolerance;
    private bool _wireBatchCommitted;

    internal string Page => Expand(list.GetProperty("page").GetString()!);
    internal string CounterpartPage => Expand(list.GetProperty("counterpart_page").GetString()!);
    internal int UndoSteps { get; private set; }
    internal int SkippedCount => _skipped.Count;
    internal bool UndoCountIncomplete { get; private set; }
    internal JsonElement? Image { get; private set; }
    internal object Report => new { page = Page, counterpartPage = CounterpartPage, undoSteps = UndoSteps, undoCountIncomplete = UndoCountIncomplete, stages = _stages, skipped = _skipped, evidence = _evidence };

    internal async Task DrawAsync()
    {
        Stage("pages");
        await WriteAsync("eplan_page_create", new() { ["pages"] = new[] { new { name = Page }, new { name = CounterpartPage } } }, ["pages"]);
        // A54: opening the editor selects LargeWrite and lets the engineer watch all subsequent batches.
        JsonElement opened = await ReadAsync("eplan_page_open", new() { ["page"] = Page });
        Assert.True(opened.GetProperty("editor_opened").GetBoolean());
        JsonElement initial = await ReadAsync("eplan_page_get", PageArgs(Page));
        double grid = initial.GetProperty("drawings").GetProperty("gridSize").GetDouble();
        Assert.True(double.IsFinite(grid) && grid > 0);
        _tolerance = grid / 2;

        Stage("location_boxes");
        foreach (JsonElement box in Items("location_boxes"))
        {
            JsonElement corners = box.GetProperty("corners");
            JsonElement? result = await WriteAsync("eplan_device_place", new()
            {
                ["page"] = Page,
                ["location_box"] = box.GetProperty("structure").GetString(),
                ["x"] = corners[0][0].GetDouble(),
                ["y"] = corners[0][1].GetDouble(),
                ["corner_x"] = corners[1][0].GetDouble(),
                ["corner_y"] = corners[1][1].GetDouble(),
            }, [Id(box)]);
            if (result is not { } placed)
            { continue; }
            JsonElement read = placed.GetProperty("locationBox");
            Assert.Equal(box.GetProperty("structure").GetString(), read.GetProperty("structure").GetString());
            Assert.Equal("dashed", read.GetProperty("outline").GetString());
            Near([corners[0][0].GetDouble(), corners[0][1].GetDouble(), corners[1][0].GetDouble(), corners[1][1].GetDouble()], read.GetProperty("bounds"));
            _placed[Id(box)] = box;
        }

        Stage("devices");
        foreach (JsonElement device in Items("devices"))
        { await PlaceDeviceAsync(device); }

        Stage("plc");
        await PlcAsync();
        Stage("terminals");
        await TerminalsAsync();

        Stage("wires_symbols_and_cables");
        await WiresAsync();
        Stage("generate");
        await WriteAsync("eplan_connection_generate", new() { ["pages"] = new[] { Page } }, ["generate"]);
        await GeneratedCablesAsync();

        Stage("interruption_points");
        await InterruptionsAsync();

        Stage("texts");
        JsonElement[] texts = Items("texts");
        if (texts.Length > 0)
        {
            await WriteAsync("eplan_placement_create", new()
            {
                ["page"] = Page,
                ["snap_to_grid"] = false,
                ["items"] = texts.Select(t => new { kind = "text", position = t.GetProperty("position"), height_mm = t.GetProperty("height_mm").GetDouble(), contents = new Dictionary<string, string> { ["??_??"] = t.GetProperty("text").GetString()! } }).ToArray(),
            }, texts.Select(Id).ToArray());
        }
        Stage("verify");
        await VerifyAsync();
    }

    internal async Task ExportAsync()
    {
        Image = await ReadAsync("eplan_export_image", new() { ["pages"] = new[] { Page }, ["format"] = "png", ["width_px"] = 2400 });
        Assert.False(Image.Value.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, Image.Value.GetProperty("fileCount").GetInt32());
        Assert.True(Image.Value.GetProperty("totalBytes").GetInt64() > 0);
        output("Page image: " + Image.Value.GetProperty("directory").GetString());
    }

    private async Task PlaceDeviceAsync(JsonElement device, string? page = null)
    {
        Dictionary<string, object?> args = SymbolArgs(device, device);
        args["page"] = page ?? Page;
        args["tag"] = Expand(device.GetProperty("tag").GetString()!);
        args["connection_designations"] = device.GetProperty("connection_designations");
        if (device.GetProperty("symbol").GetString() != "PLCC")
        { args["display_properties"] = new[] { new { property_id = 20214, offset_x_mm = 4, offset_y_mm = -4 } }; }
        JsonElement? result = await WriteAsync("eplan_device_place", args, [Id(device)]);
        if (result is not { } placed)
        { return; }
        _devicePages[Id(device)] = page ?? Page;
        await RememberDeviceAsync(device, placed);
    }

    private Dictionary<string, object?> SymbolArgs(JsonElement device, JsonElement symbol) => new()
    {
        ["page"] = Page,
        ["library"] = symbol.GetProperty("library").GetString(),
        ["symbol"] = symbol.GetProperty("symbol").GetString(),
        ["symbol_variant"] = symbol.GetProperty("variant").GetInt32(),
        ["x"] = device.GetProperty("position")[0].GetDouble(),
        ["y"] = device.GetProperty("position")[1].GetDouble(),
    };

    private async Task RememberDeviceAsync(JsonElement device, JsonElement placed, string? owner = null)
    {
        Assert.False(placed.GetProperty("recovered").GetBoolean());
        _placed[Id(device)] = device;
        _symbols[Id(device)] = placed.GetProperty("symbol");
        string actualTag = placed.GetProperty("device").GetProperty("tag").GetString()!;
        Assert.True(SameName(Expand(device.GetProperty("tag").GetString()!), actualTag));
        _actualTags[Id(device)] = actualTag;
        if (device.TryGetProperty("contact_kind", out JsonElement kind))
        { Assert.Equal(kind.GetString() == "no" ? "NoContact" : "NcContact", placed.GetProperty("device").GetProperty("category").GetString()); }
        JsonElement read = await ReadAsync("eplan_device_get", new() { ["tag"] = owner ?? actualTag, ["language"] = "??_??", ["response_format"] = "detailed" });
        RequireComplete(read);
        JsonElement actual = owner is null ? read.GetProperty("device") : Assert.Single(read.GetProperty("functions").EnumerateArray(), f => SameName(actualTag, f.GetProperty("tag").GetString()!) && f.GetProperty("page").GetString() == Page);
        string[] designations = [.. device.GetProperty("connection_designations").EnumerateArray().Select(n => n.GetString()!)];
        string[] pins = [.. actual.GetProperty("pins").EnumerateArray().Select(n => n.GetString()!)];
        if (owner is not null && pins.Length == 0)
        {
            // A placed terminal reads back without named pins (no connection_designations on terminals); wires address it by connection point order.
            Assert.Equal(_symbols[Id(device)].GetProperty("connectionPoints").GetArrayLength(), designations.Length);
            _evidence.Add(new { note = "terminal pins unnamed; mapped by symbol connection point order", tag = actualTag, pins = designations });
            pins = designations;
        }
        Assert.Equal(designations, pins);
        Assert.Equal(_symbols[Id(device)].GetProperty("connectionPoints").GetArrayLength(), pins.Length);
        _pins[Id(device)] = pins;
        Assert.Empty(read.GetProperty("parts").EnumerateArray());
        _devices[Id(device)] = actual;
    }

    private async Task PlcAsync()
    {
        JsonElement plc = list.GetProperty("plc"), box = plc.GetProperty("box");
        await PlaceDeviceAsync(box);
        if (_devices.TryGetValue(Id(box), out JsonElement card))
        { Assert.Equal("PLCBox", card.GetProperty("category").GetString()); }
        var symbols = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement point in plc.GetProperty("points").EnumerateArray())
        {
            if (!_actualTags.TryGetValue(Id(box), out string? parent))
            { Skip(Id(point), "The PLC box was skipped; a connection point requires an existing PLC parent."); continue; }
            string direction = point.GetProperty("direction").GetString()!;
            if (!symbols.TryGetValue(direction, out JsonElement symbol))
            { symbols[direction] = symbol = await InputSymbolAsync(direction); }
            Dictionary<string, object?> args = SymbolArgs(point, symbol);
            args["tag"] = parent;
            foreach (string field in new[] { "connection_point", "address", "symbolic_address" })
            { args["plc_" + field] = point.GetProperty(field).GetString(); }
            args["plc_text"] = point.GetProperty("function_text").GetString();
            JsonElement? result = await WriteAsync("eplan_device_place", args, [Id(point)]);
            if (result is not { } placed)
            { continue; }
            await RememberDeviceAsync(point, placed, parent);
            Assert.Equal("PLCTerminal", _devices[Id(point)].GetProperty("category").GetString());
            if (point.TryGetProperty("display_properties", out JsonElement displays) && displays.GetArrayLength() > 0)
            { Skip(Id(point) + "/display_properties", "A55 live T7d: display_properties are refused for PLC points because added displays do not survive commit; native PLC values are verified separately."); }
        }
        if (!_actualTags.TryGetValue(Id(box), out string? tag))
        { return; }
        JsonElement read = await ReadAsync("eplan_plc_get", new() { ["plc"] = tag, ["language"] = "??_??", ["limit"] = 200, ["response_format"] = "detailed" });
        RequireComplete(read);
        // A55: count connection-point names, not the extra device-level PLC terminals.
        JsonElement[] channels = read.GetProperty("channels").EnumerateArray().Where(c => c.GetProperty("name").GetString()!.Contains(':', StringComparison.Ordinal)).ToArray();
        JsonElement[] points = plc.GetProperty("points").EnumerateArray().Where(p => _placed.ContainsKey(Id(p))).ToArray();
        Assert.Equal(points.Length, channels.Length);
        foreach (JsonElement point in points)
        {
            JsonElement channel = Assert.Single(channels, c => SameName(_actualTags[Id(point)], c.GetProperty("name").GetString()!));
            Assert.True(SameName(tag, channel.GetProperty("owner").GetString()!));
            Assert.Equal(Page, channel.GetProperty("page").GetString());
            Assert.Equal(point.GetProperty("address").GetString(), channel.GetProperty("address").GetString());
            Assert.Equal(point.GetProperty("symbolic_address").GetString(), channel.GetProperty("manualSymbolicAddress").GetString());
            Assert.Equal(point.GetProperty("function_text").GetString(), channel.GetProperty("text").GetString());
            Assert.False(string.IsNullOrWhiteSpace(channel.GetProperty("symbolicAddress").GetString()));
            Assert.Equal(1, channel.GetProperty("parentlessRepresentations").GetInt32());
        }
    }

    private async Task<JsonElement> InputSymbolAsync(string direction)
    {
        string? cursor = null;
        do
        {
            JsonElement read = await ReadAsync("eplan_symbol_search", new() { ["kind"] = "function", ["limit"] = 200, ["cursor"] = cursor });
            foreach (JsonElement symbol in read.GetProperty("symbols").EnumerateArray())
            {
                if (symbol.GetProperty("functionCategory").GetString() == "PLCTerminal" && symbol.TryGetProperty("plcInputDefinition", out JsonElement definition) && definition.ValueKind == JsonValueKind.String
                    && symbol.GetProperty("connectionPoints").GetArrayLength() == 1 && symbol.GetProperty("connectionPoints")[0].GetProperty("direction").GetString() == direction)
                { return JsonSerializer.SerializeToElement(new { library = symbol.GetProperty("library").GetString(), symbol = symbol.GetProperty("name").GetString(), variant = symbol.GetProperty("variant").GetInt32() }); }
            }
            cursor = read.GetProperty("nextCursor").GetString();
            Assert.True(!read.GetProperty("truncated").GetBoolean() || cursor is not null, "Symbol discovery was truncated without a continuation cursor.");
        } while (cursor is not null);
        throw new InvalidOperationException("No single-pin native digital-input symbol with direction " + direction + " was discovered.");
    }

    private async Task TerminalsAsync()
    {
        JsonElement strip = list.GetProperty("terminal_strip");
        string tag = Expand(strip.GetProperty("tag").GetString()!);
        bool created = false;
        foreach (JsonElement terminal in strip.GetProperty("terminals").EnumerateArray())
        {
            var args = new Dictionary<string, object?>
            {
                ["page"] = Page,
                ["terminal_strip"] = tag,
                ["terminal_number"] = terminal.GetProperty("number").GetString(),
                ["symbol_variant"] = terminal.GetProperty("variant").GetInt32(),
                ["x"] = terminal.GetProperty("position")[0].GetDouble(),
                ["y"] = terminal.GetProperty("position")[1].GetDouble(),
            };
            if (!created)
            { args["terminal_strip_text"] = strip.GetProperty("function_text").GetString(); }
            JsonElement? result = await WriteAsync("eplan_device_place", args, created ? [Id(terminal)] : [Id(strip), Id(terminal)]);
            if (result is not { } placed)
            { continue; }
            JsonElement member = placed.GetProperty("terminal");
            Assert.True(SameName(tag, member.GetProperty("strip").GetString()!));
            Assert.True(member.GetProperty("owner").ValueKind == JsonValueKind.Null || SameName(tag, member.GetProperty("owner").GetString()!));
            Assert.Equal(!created, member.GetProperty("stripCreated").GetBoolean());
            Assert.Equal(terminal.GetProperty("number").GetString(), member.GetProperty("number").GetString());
            tag = member.GetProperty("strip").GetString()!;
            created = true;
            await RememberDeviceAsync(terminal, placed, tag);
            Assert.Equal("Terminal", _devices[Id(terminal)].GetProperty("category").GetString());
        }
        if (!created)
        { return; }
        // A55: membership is authoritative only after commit; the strip text is read in its stored language.
        JsonElement members = await ReadAsync("eplan_terminal_strip_get", new() { ["strip"] = tag, ["limit"] = 200 });
        RequireComplete(members);
        Assert.True(SameName(tag, members.GetProperty("strip").GetProperty("name").GetString()!));
        Assert.Equal(strip.GetProperty("terminals").EnumerateArray().Where(t => _placed.ContainsKey(Id(t))).Select(t => t.GetProperty("number").GetString()), members.GetProperty("terminals").EnumerateArray().Select(t => t.GetProperty("designation").GetString()));
        Assert.All(members.GetProperty("terminals").EnumerateArray(), t => Assert.Equal(Page, t.GetProperty("page").GetString()));
        string[] expectedNames = strip.GetProperty("terminals").EnumerateArray().Where(t => _placed.ContainsKey(Id(t))).Select(t => _actualTags[Id(t)]).ToArray();
        string[] actualNames = members.GetProperty("terminals").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(expectedNames.Length, actualNames.Length);
        Assert.All(expectedNames.Zip(actualNames), pair => Assert.True(SameName(pair.First, pair.Second)));
        JsonElement read = await ReadAsync("eplan_device_get", new() { ["tag"] = tag, ["language"] = "??_??", ["response_format"] = "detailed" });
        RequireComplete(read);
        Assert.Equal(strip.GetProperty("function_text").GetString(), read.GetProperty("device").GetProperty("text").GetString());
    }

    private async Task WiresAsync()
    {
        JsonElement[] drawings = await DrawingsAsync(Page);
        var items = new List<object>();
        var wireIds = new List<string>();
        bool incomplete = false;
        foreach (JsonElement wire in Items("wires"))
        {
            ResolvedPin? from = ResolvePin(wire.GetProperty("from"), wire.GetProperty("points")[0], drawings);
            JsonElement points = wire.GetProperty("points");
            ResolvedPin? to = ResolvePin(wire.GetProperty("to"), points[points.GetArrayLength() - 1], drawings);
            if (from is null || to is null)
            { Skip(Id(wire), "An endpoint or independent pin readback was skipped; no wire is drawn from guessed pin coordinates."); incomplete = true; continue; }
            if (points.GetArrayLength() == 2 && points[0].ValueKind == JsonValueKind.String && points[1].ValueKind == JsonValueKind.String)
            {
                Assert.True(Facing(from, to), $"Wire '{Id(wire)}' uses [\"from\",\"to\"] for autoconnect, but its pins do not face each other on one axis; provide explicit bend points in the drawing list.");
                _wires[Id(wire)] = (from, to, null);
                _evidence.Add(new { wire = Id(wire), mode = "autoconnect" });
                continue;
            }
            double[][] path = points.EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String
                ? p.GetString() == "from" ? from.Position : to.Position : p.EnumerateArray().Select(n => n.GetDouble()).ToArray()).ToArray();
            for (int i = 1; i < path.Length; i++)
            { Assert.True((path[i][0] == path[i - 1][0]) != (path[i][1] == path[i - 1][1]), "Resolved path must be nonzero and orthogonal; correct the drawing list."); }
            items.Add(new { kind = "wire", from = from.Reference, to = to.Reference, points = path });
            _wires[Id(wire)] = (from, to, wireIds.Count);
            wireIds.Add(Id(wire));
        }
        JsonElement[] nodes = Items("nodes");
        JsonElement[] cables = Items("cables");
        if (incomplete)
        {
            foreach (JsonElement node in nodes)
            { Skip(Id(node), "The connected wire batch is incomplete; creating its nodes could autoconnect an unintended subset."); }
            foreach (string id in _wires.Keys)
            { Skip(id, "Another path in the shared wire/node batch is unavailable; the complete topology must be drawn atomically."); }
            foreach (JsonElement cable in cables)
            { Skip(Id(cable), "The shared wire batch is incomplete; cable cores require the complete topology."); }
            return;
        }
        items.AddRange(nodes.Select(n => (object)new { kind = n.GetProperty("kind").GetString(), position = n.GetProperty("position"), directions = n.GetProperty("directions") }));
        cables = cables.Where(c => !NeedsGeneratedCores(c)).ToArray();
        items.AddRange(cables.Select(c => CableItem(c, id => new { item_index = _wires[id].ItemIndex!.Value })));
        if (items.Count == 0)
        { _wireBatchCommitted = true; return; }
        JsonElement? created = await WriteAsync("eplan_connection_create", new() { ["page"] = Page, ["items"] = items, ["response_format"] = "detailed" }, wireIds.Concat(nodes.Select(Id)).Concat(cables.Select(Id)).ToArray());
        _wireBatchCommitted = created is { } result && result.GetProperty("execution").GetString() == ExecutionState.Committed;
        if (_wireBatchCommitted && cables.Length > 0)
        { await VerifyCablesAsync(created!.Value, cables); }
    }

    private bool NeedsGeneratedCores(JsonElement cable) => cable.GetProperty("cores").EnumerateArray().Any(c => _wires[c.GetProperty("wire").GetString()!].ItemIndex is null);

    private object CableItem(JsonElement cable, Func<string, object> connection) => new
    {
        kind = "cable_definition",
        designation = Expand(cable.GetProperty("tag").GetString()!),
        type_text = cable.GetProperty("type_text").GetString(),
        core_count = cable.GetProperty("core_count").GetInt32(),
        cross_section_mm2 = cable.GetProperty("cross_section_mm2").GetDouble(),
        points = cable.GetProperty("points"),
        cores = cable.GetProperty("cores").EnumerateArray().Select(core => new
        {
            connection = connection(core.GetProperty("wire").GetString()!),
            label = core.GetProperty("label").GetString(),
            colour = core.GetProperty("colour").GetString(),
        }).ToArray(),
    };

    private async Task GeneratedCablesAsync()
    {
        if (!_wireBatchCommitted)
        { return; }
        JsonElement[] cables = Items("cables").Where(NeedsGeneratedCores).ToArray();
        if (cables.Length == 0)
        { return; }
        Stage("cables_after_generate");
        JsonElement[] connections = await ConnectionsAsync(Page, detailed: true);
        object Reference(string id)
        {
            JsonElement wire = Assert.Single(Items("wires"), w => Id(w) == id);
            bool End(JsonElement c, string expected, string end) => c.GetProperty(end).ValueKind == JsonValueKind.Object
                && SameName(_actualTags[wire.GetProperty(expected).GetProperty("item").GetString()!], c.GetProperty(end).GetProperty("device").GetString()!)
                && wire.GetProperty(expected).GetProperty("pin").GetString() == c.GetProperty(end).GetProperty("pin").GetString();
            JsonElement[] matches = connections.Where(c => End(c, "from", "from") && End(c, "to", "to") || End(c, "from", "to") && End(c, "to", "from")).ToArray();
            Assert.True(matches.Length == 1, $"Wire '{id}' requires exactly one generated connection between its expected pins; found {matches.Length}.");
            Assert.True(matches[0].TryGetProperty("locator", out JsonElement locator) && locator.ValueKind == JsonValueKind.Object, $"Wire '{id}' requires a locator from the detailed generated connection list.");
            return new { locator };
        }
        // A mixed cable is created once after generation; earlier wire item indices cannot cross calls.
        object[] items = cables.Select(c => CableItem(c, Reference)).ToArray();
        JsonElement? created = await WriteAsync("eplan_connection_create", new() { ["page"] = Page, ["items"] = items, ["response_format"] = "detailed" }, cables.Select(Id).ToArray());
        if (created is { } result && result.GetProperty("execution").GetString() == ExecutionState.Committed)
        { await VerifyCablesAsync(result, cables); }
    }

    private async Task VerifyCablesAsync(JsonElement created, JsonElement[] cables)
    {
        JsonElement[] actual = created.GetProperty("actualCores").EnumerateArray().ToArray();
        Assert.Equal(cables.Sum(c => c.GetProperty("cores").GetArrayLength()), actual.Length);
        Assert.Equal(actual.Length, actual.Select(c => c.GetProperty("oid").GetInt64()).Distinct().Count());
        foreach (JsonElement cable in cables)
        {
            string tag = Expand(cable.GetProperty("tag").GetString()!);
            JsonElement[] cores = cable.GetProperty("cores").EnumerateArray().ToArray();
            foreach (JsonElement core in cores)
            {
                JsonElement read = Assert.Single(actual, c => SameName(tag, c.GetProperty("designation").GetString()!) && c.GetProperty("label").GetString() == core.GetProperty("label").GetString());
                Assert.Equal(cable.GetProperty("type_text").GetString(), read.GetProperty("type").GetString());
                Assert.Equal(core.GetProperty("colour").GetString(), read.GetProperty("colour").GetString());
                Assert.True(read.GetProperty("oid").GetInt64() > 0);
                var wire = _wires[core.GetProperty("wire").GetString()!];
                static string Pin(ResolvedPin endpoint)
                {
                    JsonElement pin = JsonSerializer.SerializeToElement(endpoint.Reference), locator = pin.GetProperty("locator");
                    return FormattableString.Invariant($"{locator.GetProperty("type").GetInt32()}/{locator.GetProperty("oid").GetInt64()}/{pin.GetProperty("pin_index").GetInt32()}");
                }
                Assert.Equal(new[] { Pin(wire.From), Pin(wire.To) }.Order(StringComparer.Ordinal), read.GetProperty("pins").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal));
            }
            string actualTag = actual.First(c => SameName(tag, c.GetProperty("designation").GetString()!)).GetProperty("designation").GetString()!;
            JsonElement data = await ReadAsync("eplan_cable_get", new() { ["cable"] = actualTag, ["limit"] = 200, ["language"] = "??_??" });
            RequireComplete(data);
            Assert.Null(data.GetProperty("nextCursor").GetString());
            JsonElement definition = data.GetProperty("cable");
            Assert.True(SameName(tag, definition.GetProperty("tag").GetString()!));
            Assert.Equal(cable.GetProperty("type_text").GetString(), definition.GetProperty("type").GetString());
            Assert.Equal(cable.GetProperty("core_count").GetRawText(), definition.GetProperty("wireCount").GetString());
            Assert.Equal(cable.GetProperty("cross_section_mm2").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture), definition.GetProperty("crossSection").GetString()!.Replace(',', '.'));
            JsonElement[] stored = data.GetProperty("cores").EnumerateArray().ToArray();
            Assert.Equal(cores.Length, stored.Length);
            foreach (JsonElement core in cores)
            {
                JsonElement read = Assert.Single(stored, c => c.GetProperty("designation").GetString() == core.GetProperty("label").GetString());
                Assert.Equal(core.GetProperty("colour").GetString(), read.GetProperty("colour").GetString());
                JsonElement wire = Assert.Single(Items("wires"), w => Id(w) == core.GetProperty("wire").GetString());
                bool End(string expected, string end) => SameName(_actualTags[wire.GetProperty(expected).GetProperty("item").GetString()!], read.GetProperty(end).GetProperty("device").GetString()!) && wire.GetProperty(expected).GetProperty("pin").GetString() == read.GetProperty(end).GetProperty("pin").GetString();
                Assert.True(End("from", "from") && End("to", "to") || End("from", "to") && End("to", "from"));
            }
        }
    }

    private ResolvedPin? ResolvePin(JsonElement endpoint, JsonElement expected, JsonElement[] drawings)
    {
        string id = endpoint.GetProperty("item").GetString()!;
        if (!_devices.TryGetValue(id, out JsonElement device))
        { return null; }
        JsonElement row = Function(drawings, _actualTags[id]);
        string[] pins = _pins[id];
        int index = Array.IndexOf(pins, endpoint.GetProperty("pin").GetString());
        Assert.True(index >= 0);
        JsonElement point = _symbols[id].GetProperty("connectionPoints")[index], local = point.GetProperty("location");
        double[] actual = [row.GetProperty("location")[0].GetDouble() + local[0].GetDouble(), row.GetProperty("location")[1].GetDouble() + local[1].GetDouble()];
        // Locators come from the latest page_get; pin order and local geometry come from the verified variant.
        if (expected.ValueKind != JsonValueKind.String)
        { Near(actual, expected); }
        return new(new { locator = row.GetProperty("locator"), pin_index = index }, actual, point.GetProperty("direction").GetString()!);
    }

    private static bool Facing(ResolvedPin from, ResolvedPin to) => from.Direction switch
    {
        "Up" => to.Direction == "Down" && from.Position[0] == to.Position[0] && from.Position[1] < to.Position[1],
        "Down" => to.Direction == "Up" && from.Position[0] == to.Position[0] && from.Position[1] > to.Position[1],
        "Left" => to.Direction == "Right" && from.Position[1] == to.Position[1] && from.Position[0] > to.Position[0],
        "Right" => to.Direction == "Left" && from.Position[1] == to.Position[1] && from.Position[0] < to.Position[0],
        _ => false,
    };

    private async Task InterruptionsAsync()
    {
        JsonElement[] points = Items("interruption_points");
        if (points.Length == 0)
        { return; }
        foreach ((string page, string peer, string position, string direction, string key) in new[]
        {
            (Page, CounterpartPage, "position", "direction", "page"),
            (CounterpartPage, Page, "counterpart_position", "counterpart_direction", "counterpart_page"),
        })
        {
            JsonElement support = list.GetProperty("interruption_support").GetProperty(key), device = support.GetProperty("device");
            await PlaceDeviceAsync(device, page);
            string[] ids = points.Select(p => Id(p) + (page == Page ? "/page" : "/counterpart")).ToArray();
            if (!_devices.ContainsKey(Id(device)))
            { foreach (string id in ids) { Skip(id, "The explicit interruption generation support device was skipped."); } continue; }
            var items = points.Select(p => (object)new { kind = "interruption_point", name = Expand(p.GetProperty("name").GetString()!), position = p.GetProperty(position), direction = p.GetProperty(direction).GetString(), counterpart_pages = new[] { peer } }).ToList();
            JsonElement node = support.GetProperty("potential_point");
            // A55/T7b: each page batch needs an independent real generated connection, even for isolated pairs.
            items.Add(new { kind = "potential_point", position = node.GetProperty("position"), directions = node.GetProperty("directions") });
            JsonElement? result = await WriteAsync("eplan_connection_create", new()
            {
                ["page"] = page,
                ["response_format"] = "detailed",
                ["items"] = items,
            }, ids);
            if (result is not { } read)
            { continue; }
            RequireComplete(read);
            Assert.Equal(points.Length * (page == Page ? 1 : 2), read.GetProperty("interruptionPoints").GetArrayLength());
            _interruptionSupport[page] = (_actualTags[Id(device)], device.GetProperty("connection_designations")[0].GetString()!);
            foreach (JsonElement point in points)
            {
                string name = Expand(point.GetProperty("name").GetString()!);
                JsonElement actual = Assert.Single(read.GetProperty("interruptionPoints").EnumerateArray(), p => p.GetProperty("page").GetString() == page && p.GetProperty("name").GetString() == name);
                Assert.Equal(name, actual.GetProperty("name").GetString());
                Assert.Equal(point.GetProperty(direction).GetString(), actual.GetProperty("direction").GetString());
                Near(point.GetProperty(position).EnumerateArray().Select(n => n.GetDouble()).ToArray(), actual.GetProperty("position"));
                if (page == CounterpartPage)
                {
                    foreach (JsonElement pair in read.GetProperty("interruptionPoints").EnumerateArray().Where(p => p.GetProperty("name").GetString() == name))
                    {
                        JsonElement counterpart = Assert.Single(pair.GetProperty("counterparts").EnumerateArray());
                        Assert.Equal(name, counterpart.GetProperty("name").GetString());
                        Assert.Equal(pair.GetProperty("page").GetString() == Page ? CounterpartPage : Page, counterpart.GetProperty("page").GetString());
                        JsonElement peerPoint = Assert.Single(read.GetProperty("interruptionPoints").EnumerateArray(), p => p.GetProperty("name").GetString() == name && p.GetProperty("page").GetString() == counterpart.GetProperty("page").GetString());
                        Assert.Equal(peerPoint.GetProperty("oid").GetInt64(), counterpart.GetProperty("oid").GetInt64());
                    }
                    // A55: 24300 may be empty after commit; native counterparts are the acceptance evidence.
                }
            }
        }
    }

    private async Task VerifyAsync()
    {
        await InsidePageAsync(Page);
        await InsidePageAsync(CounterpartPage);
        JsonElement[] drawings = await DrawingsAsync(Page);
        foreach (JsonElement device in _devices.Keys.Where(id => !_devicePages.TryGetValue(id, out string? page) || page == Page).Select(id => _placed[id]))
        {
            JsonElement row = Function(drawings, _actualTags[Id(device)]);
            Near(device.GetProperty("position").EnumerateArray().Select(n => n.GetDouble()).ToArray(), row.GetProperty("location"));
        }
        foreach (JsonElement text in Items("texts"))
        {
            string stored = "??_??@" + text.GetProperty("text").GetString() + ";";
            JsonElement actual = Assert.Single(drawings, d => d.GetProperty("kind").GetString() == "Text" && d.GetProperty("properties").GetProperty("text").GetString() == stored);
            Near(text.GetProperty("position").EnumerateArray().Select(n => n.GetDouble()).ToArray(), actual.GetProperty("location"));
        }
        JsonElement[] connections = await ConnectionsAsync(Page);
        connections = WithoutInterruptionSupport(Page, connections);
        if (_interruptionSupport.ContainsKey(CounterpartPage))
        {
            Assert.Empty(WithoutInterruptionSupport(CounterpartPage, await ConnectionsAsync(CounterpartPage)));
            JsonElement[] peers = await DrawingsAsync(CounterpartPage);
            JsonElement device = list.GetProperty("interruption_support").GetProperty("counterpart_page").GetProperty("device");
            Near(device.GetProperty("position").EnumerateArray().Select(n => n.GetDouble()).ToArray(), Function(peers, _actualTags[Id(device)]).GetProperty("location"));
        }
        string Endpoint(JsonElement endpoint) => JsonSerializer.Serialize(new[] { endpoint.GetProperty("item").GetString(), endpoint.GetProperty("pin").GetString() });
        string ReadEndpoint(JsonElement endpoint)
        {
            Assert.NotEqual(JsonValueKind.Null, endpoint.ValueKind);
            string tag = endpoint.GetProperty("device").GetString()!;
            string id = Assert.Single(_actualTags, pair => SameName(pair.Value, tag)).Key;
            return JsonSerializer.Serialize(new[] { id, endpoint.GetProperty("pin").GetString() });
        }
        JsonElement[] expectedConnections = Items("expected_connections");
        JsonElement[] supported = expectedConnections.Where(c => _wireBatchCommitted && _devices.ContainsKey(c.GetProperty("from").GetProperty("item").GetString()!) && _devices.ContainsKey(c.GetProperty("to").GetProperty("item").GetString()!)).ToArray();
        if (supported.Length != expectedConnections.Length)
        { Skip("connection_set", "Some expected endpoints were skipped; compare all observable connections against the supported subset and retain incomplete whole-page status."); }
        string[] expected = supported.Select(c => Pair(Endpoint(c.GetProperty("from")), Endpoint(c.GetProperty("to")))).Order(StringComparer.Ordinal).ToArray();
        string[] actualPairs = connections.Select(c => Pair(ReadEndpoint(c.GetProperty("from")), ReadEndpoint(c.GetProperty("to")))).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actualPairs);
    }

    private JsonElement[] WithoutInterruptionSupport(string page, JsonElement[] connections)
    {
        if (!_interruptionSupport.TryGetValue(page, out var support))
        { return connections; }
        bool Support(JsonElement c) => c.GetProperty("from").ValueKind == JsonValueKind.Null && c.GetProperty("to").ValueKind == JsonValueKind.Object
            && SameName(support.Tag, c.GetProperty("to").GetProperty("device").GetString()!) && c.GetProperty("to").GetProperty("pin").GetString() == support.Pin;
        Assert.Single(connections, Support);
        return connections.Where(c => !Support(c)).ToArray();
    }

    private async Task<JsonElement[]> DrawingsAsync(string page)
    {
        var rows = new List<JsonElement>();
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = PageArgs(page);
            if (cursor is not null)
            { args["drawing_cursor"] = cursor; }
            JsonElement data = (await ReadAsync("eplan_page_get", args)).GetProperty("drawings");
            Assert.Equal(_tolerance * 2, data.GetProperty("gridSize").GetDouble());
            rows.AddRange(data.GetProperty("items").EnumerateArray());
            cursor = data.GetProperty("nextCursor").GetString();
            Assert.Equal(cursor is not null, data.GetProperty("truncated").GetBoolean());
        } while (cursor is not null);
        return rows.ToArray();
    }

    // Acceptance trial (2026-10-30): PLC points and a strip definition once left visible objects at the page origin,
    // outside the frame, while every per-object readback passed. Nothing drawn may lie outside the page.
    private async Task InsidePageAsync(string page)
    {
        double[] size = [.. (await ReadAsync("eplan_page_get", PageArgs(page))).GetProperty("drawings").GetProperty("pageSize").EnumerateArray().Select(v => v.GetDouble())];
        foreach (JsonElement row in await DrawingsAsync(page))
        {
            double[] location = [.. row.GetProperty("location").EnumerateArray().Select(v => v.GetDouble())];
            double[] box = row.TryGetProperty("bounds", out JsonElement b) && b.ValueKind == JsonValueKind.Array ? [.. b.EnumerateArray().Select(v => v.GetDouble())] : [];
            // An empty property text reports zero bounds (A55); its location still has to be on the page.
            double[] area = box.Length == 4 && (box[2] > box[0] || box[3] > box[1]) ? box : [location[0], location[1], location[0], location[1]];
            if (!(area[0] >= 0 && area[1] >= 0 && area[2] <= size[0] && area[3] <= size[1]))
            { Assert.Fail($"{row.GetProperty("kind").GetString()} {row.GetProperty("locator").GetProperty("type").GetInt32()}/{row.GetProperty("locator").GetProperty("oid").GetInt64()} on {page} lies outside the page at [{string.Join(", ", area)}]."); }
        }
    }

    private async Task<JsonElement[]> ConnectionsAsync(string page, bool detailed = false)
    {
        var rows = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var args = new Dictionary<string, object?> { ["page"] = page, ["limit"] = 200 };
            if (detailed)
            { args["response_format"] = "detailed"; }
            if (cursor is not null)
            { args["cursor"] = cursor; }
            JsonElement data = await ReadAsync("eplan_connection_list", args);
            rows.AddRange(data.GetProperty("connections").EnumerateArray());
            cursor = data.GetProperty("nextCursor").GetString();
            Assert.True(!data.GetProperty("truncated").GetBoolean() || cursor is not null, "Connection evidence was truncated without a continuation cursor.");
        } while (cursor is not null);
        return rows.ToArray();
    }

    private async Task<JsonElement?> WriteAsync(string tool, Dictionary<string, object?> args, string[] ids)
    {
        (bool succeeded, JsonElement result) = await InvokeAsync(tool, args);
        if (!succeeded && result.GetProperty("reason").GetString() == ErrorReason.CapabilityMissing && result.GetProperty("execution").GetString() == ExecutionState.NotStarted)
        { foreach (string id in ids) { Skip(id, tool + ": " + result.GetRawText()); } return null; }
        if (!succeeded)
        {
            UndoCountIncomplete = result.TryGetProperty("execution", out JsonElement state) && state.GetString() == ExecutionState.Unknown;
            throw new InvalidOperationException(tool + " failed; no retry: " + result.GetRawText());
        }
        string execution = result.GetProperty("execution").GetString()!;
        Assert.True(execution is ExecutionState.Committed or ExecutionState.NotStarted, result.GetRawText());
        if (execution == ExecutionState.Committed)
        { UndoSteps += result.TryGetProperty("undoSteps", out JsonElement steps) ? steps.GetInt32() : tool == "eplan_placement_create" || result.TryGetProperty("undo", out JsonElement undo) && undo.ValueKind == JsonValueKind.String ? 1 : throw new InvalidOperationException("Committed write has no undo-step evidence."); }
        RequireComplete(result);
        return result;
    }

    private async Task<JsonElement> ReadAsync(string tool, Dictionary<string, object?> args)
    {
        (bool succeeded, JsonElement result) = await InvokeAsync(tool, args);
        Assert.True(succeeded, tool + ": " + result.GetRawText());
        return result;
    }

    private async Task<(bool Succeeded, JsonElement Result)> InvokeAsync(string tool, Dictionary<string, object?> args)
    {
        args["project"] = project;
        (bool succeeded, JsonElement result) = await call(tool, args);
        _evidence.Add(new { tool, succeeded, result });
        return (succeeded, result);
    }

    private static Dictionary<string, object?> PageArgs(string page) => new() { ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" };
    private JsonElement Function(JsonElement[] drawings, string tag) => Assert.Single(drawings, d => d.GetProperty("kind").GetString() != "PropertyPlacement" && d.GetProperty("properties").TryGetProperty("name", out JsonElement name) && SameName(tag, name.GetString()!));
    private bool SameName(string requested, string actual) => CableDefinitionPlanner.SameName(requested, actual, Page);
    private JsonElement[] Items(string key) => list.GetProperty(key).EnumerateArray().ToArray();
    private string Expand(string value) => value.Replace("{run}", run, StringComparison.Ordinal).Replace("{next}", next ?? run + "2", StringComparison.Ordinal);
    private static string Id(JsonElement item) => item.GetProperty("id").GetString()!;
    private void Stage(string name) { _stages.Add(name); output("Drawing batch: " + name); }
    private void Skip(string id, string reason) { _skipped.Add(new { item = id, status = "skipped", reason }); output("Skipped " + id + ": " + reason); }
    private static void RequireComplete(JsonElement result) => Assert.False(result.TryGetProperty("truncated", out JsonElement flag) && flag.GetBoolean(), "Acceptance evidence must not be truncated.");
    private static string Pair(string a, string b) => JsonSerializer.Serialize(new[] { a, b }.Order(StringComparer.Ordinal));
    private void Near(double[] expected, JsonElement actual)
    {
        Assert.Equal(expected.Length, actual.GetArrayLength());
        for (int i = 0; i < expected.Length; i++)
        { Assert.InRange(Math.Abs(expected[i] - actual[i].GetDouble()), 0, _tolerance); }
    }

    private sealed record ResolvedPin(object Reference, double[] Position, string Direction);
}

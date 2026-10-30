using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

internal sealed class AcceptanceRedrawDriver(
    Func<string, Dictionary<string, object?>, Task<(bool Succeeded, JsonElement Result)>> call,
    string project, JsonElement list, string run, Action<string> output)
{
    private readonly Dictionary<string, JsonElement> _placed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _actualTags = new(StringComparer.Ordinal);
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
        JsonElement plc = list.GetProperty("plc");
        Skip(Id(plc.GetProperty("box")), "eplan_device_place only accepts ordinary Function symbols; PLCBox is rejected by DeviceSymbolPlan. No PLC parent creation contract is registered.");
        foreach (JsonElement point in plc.GetProperty("points").EnumerateArray())
        { Skip(Id(point), "PLC parent creation is unavailable; device_place has no connection_point/address/symbolic_address/function_text inputs. PLC display_properties cannot substitute a native point."); }

        Stage("terminals");
        JsonElement strip = list.GetProperty("terminal_strip");
        Skip(Id(strip), "eplan_device_place has no terminal-strip creation or function-text input.");
        foreach (JsonElement terminal in strip.GetProperty("terminals").EnumerateArray())
        { Skip(Id(terminal), "Parent strip was skipped; ordinary Function placement cannot establish TerminalStrip.Terminals membership or terminal numbers."); }

        Stage("wires_and_symbols");
        await WiresAsync();
        Stage("generate");
        await WriteAsync("eplan_connection_generate", new() { ["pages"] = new[] { Page } }, ["generate"]);

        Stage("cables");
        foreach (JsonElement cable in Items("cables"))
        {
            // A55 requires current connection locators for an already generated wire. Connection-list endpoints
            // grant no locator/digest; reusing a function locator or replaying a wire would misidentify the core.
            Skip(Id(cable), "Generated connections have no locator/digest in eplan_connection_list. Existing-wire cable_definition cannot be safely addressed after the required generate stage.");
        }

        Stage("interruption_points");
        foreach (JsonElement point in Items("interruption_points"))
        { await InterruptionAsync(point); }

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

    private async Task PlaceDeviceAsync(JsonElement device)
    {
        JsonElement position = device.GetProperty("position");
        var args = new Dictionary<string, object?>
        {
            ["page"] = Page,
            ["tag"] = Expand(device.GetProperty("tag").GetString()!),
            ["library"] = device.GetProperty("library").GetString(),
            ["symbol"] = device.GetProperty("symbol").GetString(),
            ["symbol_variant"] = device.GetProperty("variant").GetInt32(),
            ["x"] = position[0].GetDouble(),
            ["y"] = position[1].GetDouble(),
            ["connection_designations"] = device.GetProperty("connection_designations"),
            ["display_properties"] = new[] { new { property_id = 20214, offset_x_mm = 4, offset_y_mm = -4 } },
        };
        JsonElement? result = await WriteAsync("eplan_device_place", args, [Id(device)]);
        if (result is not { } placed)
        { return; }
        Assert.False(placed.GetProperty("recovered").GetBoolean());
        _placed[Id(device)] = device;
        _symbols[Id(device)] = placed.GetProperty("symbol");
        string actualTag = placed.GetProperty("device").GetProperty("tag").GetString()!;
        Assert.True(SameName(Expand(device.GetProperty("tag").GetString()!), actualTag));
        _actualTags[Id(device)] = actualTag;
        if (device.TryGetProperty("contact_kind", out JsonElement kind))
        { Assert.Equal(kind.GetString() == "no" ? "NoContact" : "NcContact", placed.GetProperty("device").GetProperty("category").GetString()); }
        (bool succeeded, JsonElement read) = await InvokeAsync("eplan_device_get", new() { ["tag"] = actualTag });
        if (!succeeded && read.GetProperty("reason").GetString() == ErrorReason.DeviceNotFound && device.TryGetProperty("contact_kind", out _))
        {
            Skip(Id(device) + "/pins", "The placed contact is not exposed as a main function by eplan_device_get; independent pin-designation readback is unavailable.");
            return;
        }
        Assert.True(succeeded, read.GetRawText());
        Assert.False(read.GetProperty("truncated").GetBoolean());
        Assert.Equal(device.GetProperty("connection_designations").EnumerateArray().Select(n => n.GetString()), read.GetProperty("device").GetProperty("pins").EnumerateArray().Select(n => n.GetString()));
        Assert.Empty(read.GetProperty("parts").EnumerateArray());
        _devices[Id(device)] = read.GetProperty("device");
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
            double[][] path = points.EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String
                ? p.GetString() == "from" ? from.Position : to.Position : p.EnumerateArray().Select(n => n.GetDouble()).ToArray()).ToArray();
            for (int i = 1; i < path.Length; i++)
            { Assert.True((path[i][0] == path[i - 1][0]) != (path[i][1] == path[i - 1][1]), "Resolved path must be nonzero and orthogonal; correct the drawing list."); }
            items.Add(new { kind = "wire", from = from.Reference, to = to.Reference, points = path });
            wireIds.Add(Id(wire));
        }
        JsonElement[] nodes = Items("nodes");
        if (incomplete)
        {
            foreach (JsonElement node in nodes)
            { Skip(Id(node), "The connected wire batch is incomplete; creating its nodes could autoconnect an unintended subset."); }
            foreach (string id in wireIds)
            { Skip(id, "Another path in the shared wire/node batch is unavailable; the complete topology must be drawn atomically."); }
            return;
        }
        items.AddRange(nodes.Select(n => (object)new { kind = n.GetProperty("kind").GetString(), position = n.GetProperty("position"), directions = n.GetProperty("directions") }));
        if (items.Count == 0)
        { return; }
        JsonElement? created = await WriteAsync("eplan_connection_create", new() { ["page"] = Page, ["items"] = items, ["response_format"] = "detailed" }, wireIds.Concat(nodes.Select(Id)).ToArray());
        _wireBatchCommitted = created is { } result && result.GetProperty("execution").GetString() == ExecutionState.Committed;
    }

    private ResolvedPin? ResolvePin(JsonElement endpoint, JsonElement expected, JsonElement[] drawings)
    {
        string id = endpoint.GetProperty("item").GetString()!;
        if (!_devices.TryGetValue(id, out JsonElement device))
        { return null; }
        JsonElement row = Function(drawings, _actualTags[id]);
        string[] pins = device.GetProperty("pins").EnumerateArray().Select(n => n.GetString()!).ToArray();
        int index = Array.IndexOf(pins, endpoint.GetProperty("pin").GetString());
        Assert.True(index >= 0);
        JsonElement local = _symbols[id].GetProperty("connectionPoints")[index].GetProperty("location");
        double[] actual = [row.GetProperty("location")[0].GetDouble() + local[0].GetDouble(), row.GetProperty("location")[1].GetDouble() + local[1].GetDouble()];
        // Locators come from the latest page_get; pin order and local geometry come from the verified variant.
        if (expected.ValueKind != JsonValueKind.String)
        { Near(actual, expected); }
        return new(new { locator = row.GetProperty("locator"), pin_index = index }, actual);
    }

    private async Task InterruptionAsync(JsonElement point)
    {
        string name = Expand(point.GetProperty("name").GetString()!);
        foreach ((string page, string peer, string position, string direction) in new[]
        {
            (Page, CounterpartPage, "position", "direction"),
            (CounterpartPage, Page, "counterpart_position", "counterpart_direction"),
        })
        {
            JsonElement? result = await WriteAsync("eplan_connection_create", new()
            {
                ["page"] = page,
                ["response_format"] = "detailed",
                ["items"] = new[] { new { kind = "interruption_point", name, position = point.GetProperty(position), direction = point.GetProperty(direction).GetString(), counterpart_pages = new[] { peer } } },
            }, [Id(point) + (page == Page ? "/page" : "/counterpart")]);
            if (result is not { } read)
            { continue; }
            RequireComplete(read);
            JsonElement actual = Assert.Single(read.GetProperty("interruptionPoints").EnumerateArray(), p => p.GetProperty("page").GetString() == page);
            Assert.Equal(name, actual.GetProperty("name").GetString());
            Near(point.GetProperty(position).EnumerateArray().Select(n => n.GetDouble()).ToArray(), actual.GetProperty("position"));
            if (page == CounterpartPage)
            {
                foreach (JsonElement pair in read.GetProperty("interruptionPoints").EnumerateArray())
                {
                    JsonElement counterpart = Assert.Single(pair.GetProperty("counterparts").EnumerateArray());
                    Assert.Equal(name, counterpart.GetProperty("name").GetString());
                    Assert.NotEqual(pair.GetProperty("page").GetString(), counterpart.GetProperty("page").GetString());
                }
                // A55: 24300 may be empty after commit; native counterparts are the acceptance evidence.
            }
        }
    }

    private async Task VerifyAsync()
    {
        JsonElement[] drawings = await DrawingsAsync(Page);
        foreach (JsonElement device in Items("devices").Where(d => _placed.ContainsKey(Id(d))))
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

    private async Task<JsonElement[]> ConnectionsAsync(string page)
    {
        var rows = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var args = new Dictionary<string, object?> { ["page"] = page, ["limit"] = 200 };
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
    private string Expand(string value) => value.Replace("{run}", run, StringComparison.Ordinal);
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

    private sealed record ResolvedPin(object Reference, double[] Position);
}

using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    private static readonly int[] _plcDisplayIds = [20400, 20403, 20011, 20301];
    [Fact]
    public async Task TwoPlcInputsAndNoNcContacts_ReadNativeValuesDisplaysAndRepresentations_ThenEachUndoRestores()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        string suffix = page[Prefix.Length..];
        LiveSnapshots.Snapshot original = await selection.SnapshotAsync();
        var snapshots = new List<string> { LiveSnapshots.Hash([original.Page(page)]) };
        var discovered = new Dictionary<string, (List<JsonElement> Symbols, string? Cursor)>(StringComparer.Ordinal);

        async Task<JsonElement> Find(string kind, Func<JsonElement, bool> matches)
        {
            if (!discovered.TryGetValue(kind, out var scan))
            { scan = ([], null); }
            foreach (JsonElement symbol in scan.Symbols)
            { if (matches(symbol)) { return symbol; } }
            if (scan.Symbols.Count > 0 && scan.Cursor is null)
            { throw new Xunit.Sdk.XunitException("Required native symbol was not discovered."); }
            string? cursor = scan.Cursor;
            do
            {
                JsonElement result = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = project, ["kind"] = kind, ["library"] = kind == "plc_box" ? "SPECIAL" : null, ["name_contains"] = kind == "plc_box" ? "PLCC" : null, ["limit"] = 200, ["cursor"] = cursor });
                JsonElement[] symbols = [.. result.GetProperty("symbols").EnumerateArray()];
                scan.Symbols.AddRange(symbols);
                cursor = result.GetProperty("nextCursor").GetString();
                discovered[kind] = (scan.Symbols, cursor);
                foreach (JsonElement symbol in symbols)
                {
                    if (matches(symbol))
                    { return symbol; }
                }
            }
            while (cursor is not null);
            throw new Xunit.Sdk.XunitException("Required native symbol was not discovered.");
        }
        static string? Text(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        JsonElement cardSymbol = await Find("plc_box", s => Text(s, "library") == "SPECIAL" && Text(s, "name") == "PLCC" && s.GetProperty("variant").GetInt32() == 0);
        JsonElement inputSymbol = await Find("function", s => Text(s, "functionCategory") == "PLCTerminal" && Text(s, "plcInputDefinition") is not null && s.GetProperty("connectionPoints").GetArrayLength() == 1);
        Dictionary<string, object?> Args(JsonElement symbol, string tag, double x, double y) => new()
        {
            ["project"] = project,
            ["page"] = page,
            ["tag"] = tag,
            ["x"] = x,
            ["y"] = y,
            ["library"] = Text(symbol, "library"),
            ["symbol"] = Text(symbol, "name"),
            ["symbol_variant"] = symbol.GetProperty("variant").GetInt32()
        };
        async Task<JsonElement> Place(Dictionary<string, object?> args)
        {
            await eplan.CallAsync("eplan_device_place", new(args) { ["dry_run"] = true });
            Assert.Equal(snapshots[^1], (await selection.SnapshotAsync(page)).Hash);
            JsonElement placed = await eplan.CallAsync("eplan_device_place", args);
            Assert.Equal((ExecutionState.Committed, "AI: eplan_device_place"), (Text(placed, "execution"), Text(placed, "undo")));
            snapshots.Add((await selection.SnapshotAsync(page)).Hash);
            return placed;
        }
        JsonElement card = await Place(Args(cardSymbol, "-KFT" + suffix, 40, 100));
        string tag = card.GetProperty("device").GetProperty("tag").GetString()!;
        JsonElement cardRead = await eplan.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag });
        Assert.Equal(("PLCBox", page), (Text(cardRead.GetProperty("device"), "category"), Text(cardRead.GetProperty("device"), "page")));
        var pointArgs = new List<Dictionary<string, object?>>();
        for (int i = 0; i < 2; i++)
        {
            string number = i.ToString(CultureInfo.InvariantCulture);
            Dictionary<string, object?> args = Args(inputSymbol, tag, 100 + 60 * i, 180);
            args["plc_connection_point"] = i == 0 ? "X10:2" : "DI a.0";
            args["plc_address"] = "I0." + number;
            args["plc_symbolic_address"] = "Input_" + number;
            args["plc_text"] = "Fictional input " + number;
            args["display_properties"] = JsonSerializer.SerializeToElement(_plcDisplayIds.Select((id, row) => new { property_id = id, offset_x_mm = 0, offset_y_mm = -8 - 8 * row }));
            JsonElement placed = await Place(args);
            Assert.Equal(JsonValueKind.Null, placed.GetProperty("functions").ValueKind);
            Assert.Equal(args["plc_address"], Text(placed.GetProperty("plc"), "address"));
            pointArgs.Add(args);
        }
        JsonElement plc = await eplan.CallAsync("eplan_plc_get", new() { ["project"] = project, ["plc"] = tag, ["language"] = "??_??", ["limit"] = 100, ["response_format"] = "detailed" });
        Assert.Equal(2, plc.GetProperty("total").GetInt32());
        JsonElement drawings = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true })).GetProperty("drawings").GetProperty("items");
        foreach (Dictionary<string, object?> args in pointArgs)
        {
            JsonElement channel = Assert.Single(plc.GetProperty("channels").EnumerateArray(), c => Text(c, "address") == (string)args["plc_address"]!);
            Assert.Equal((tag, page, (string)args["plc_symbolic_address"]!, (string)args["plc_text"]!), (Text(channel, "owner"), Text(channel, "page"), Text(channel, "manualSymbolicAddress"), Text(channel, "text")));
            Assert.Equal(args["plc_symbolic_address"], Text(channel, "symbolicAddress"));
            Assert.Equal(1, channel.GetProperty("parentlessRepresentations").GetInt32());
            JsonElement device = await eplan.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = Text(channel, "name"), ["response_format"] = "detailed", ["language"] = "??_??" });
            JsonElement[] functions = [device.GetProperty("device"), .. device.GetProperty("functions").EnumerateArray()];
            Assert.Contains(functions, f => Text(f, "category") == "PLCTerminal" && Text(f, "page") == page && f.GetProperty("pins").EnumerateArray().Any(p => p.GetString() == (string)args["plc_connection_point"]!));
            static string? Property(JsonElement read, int id) => read.GetProperty("properties").EnumerateArray().Where(p => p.GetProperty("id").ValueKind == JsonValueKind.Number && p.GetProperty("id").GetInt32() == id).Select(p => p.GetProperty("value").GetString()).FirstOrDefault();
            Assert.Equal(Property(device, 20301), Text(channel, "cardReference")); // Empty is valid without an overview counterpart; never synthesize the page.
            string?[] values = [(string)args["plc_address"]!, Text(channel, "symbolicAddress"), (string)args["plc_text"]!, Text(channel, "cardReference")];
            for (int row = 0; row < values.Length; row++)
            {
                JsonElement display = Assert.Single(drawings.EnumerateArray(), d => Text(d, "kind") == "PropertyPlacement" && d.GetProperty("location")[0].GetDouble() == (double)args["x"]! && d.GetProperty("location")[1].GetDouble() == 180 - 8 - 8 * row);
                Assert.Equal(values[row] ?? "", Text(display.GetProperty("properties"), "display") ?? "");
            }
        }
        foreach (string field in new[] { "plc_connection_point", "plc_address" })
        {
            var duplicate = new Dictionary<string, object?>(pointArgs[0]);
            duplicate[field == "plc_address" ? "plc_connection_point" : "plc_address"] = field == "plc_address" ? "Other" : "I1.0";
            JsonElement error = await eplan.ErrorAsync("eplan_device_place", duplicate);
            Assert.Equal(ErrorReason.AlreadyExists, Text(error, "reason"));
            Assert.Equal(snapshots[^1], (await selection.SnapshotAsync(page)).Hash);
        }
        foreach ((string category, string[] pins, int x) in new[] { ("NoContact", new[] { "13", "14" }, 100), ("NcContact", new[] { "11", "12" }, 160) })
        {
            JsonElement symbol = await Find("function", s => Text(s, "functionCategory") == category && s.GetProperty("connectionPoints").GetArrayLength() == 2);
            Dictionary<string, object?> args = Args(symbol, "-ST" + suffix + category, x, 80);
            args["connection_designations"] = pins;
            JsonElement contact = await Place(args);
            string contactTag = contact.GetProperty("device").GetProperty("tag").GetString()!;
            JsonElement read = (await eplan.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = contactTag })).GetProperty("device");
            Assert.Equal(category, Text(read, "category"));
            Assert.Equal(pins, read.GetProperty("pins").EnumerateArray().Select(p => p.GetString()));
        }
        for (int i = snapshots.Count - 2; i >= 0; i--)
        {
            await selection.UndoAsync();
            Assert.Equal(snapshots[i], (await selection.SnapshotAsync(page)).Hash);
            if (i is 1 or 2)
            {
                JsonElement remaining = await eplan.CallAsync("eplan_plc_get", new() { ["project"] = project, ["plc"] = tag, ["response_format"] = "detailed" });
                Assert.Equal(i - 1, remaining.GetProperty("total").GetInt32());
                Assert.All(remaining.GetProperty("channels").EnumerateArray(), c => Assert.Equal(1, c.GetProperty("parentlessRepresentations").GetInt32()));
                JsonElement names = await eplan.CallAsync("eplan_device_list", new() { ["project"] = project, ["category"] = "PLCTerminal", ["tag_contains"] = tag, ["include_auxiliary"] = true });
                Assert.Equal(i - 1, names.GetProperty("total").GetInt32()); // Also detects an orphaned implicit representation after undo.
            }
        }
        Assert.Equal(0, await DevicesOnAsync(project, page));
        Assert.Equal(original.Hash, (await selection.SnapshotAsync()).Hash);
    }
}

using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task TerminalStripWithThreeTerminals_ReadsOwnershipAndSequence_SplitsConnections_AndEachCallUndoesOnce()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        string suffix = page[Prefix.Length..];
        string strip = "=GAA+A1-XT" + suffix;
        // "X" also matches hundreds of other names, so page until the exact variant 0 appears.
        async Task<JsonElement> Symbol(string name)
        {
            string? cursor = null;
            do
            {
                Dictionary<string, object?> args = new() { ["project"] = project, ["library"] = "IEC_symbol", ["name_contains"] = name, ["limit"] = 200 };
                if (cursor is not null)
                { args["cursor"] = cursor; }
                JsonElement page = await eplan.CallAsync("eplan_symbol_search", args);
                foreach (JsonElement s in page.GetProperty("symbols").EnumerateArray())
                {
                    if (s.GetProperty("name").GetString() == name && s.GetProperty("variant").GetInt32() == 0)
                    { return s; }
                }
                cursor = page.TryGetProperty("nextCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            }
            while (cursor is not null);
            throw new Xunit.Sdk.XunitException($"Symbol {name} variant 0 not found in IEC_symbol.");
        }
        JsonElement terminalSymbol = await Symbol("X"), endpointSymbol = await Symbol("AS");
        static JsonElement Pin(JsonElement symbol, string direction) => Assert.Single(symbol.GetProperty("connectionPoints").EnumerateArray(), p => p.GetProperty("direction").GetString() == direction).GetProperty("location");
        JsonElement up = Pin(terminalSymbol, "Up"), down = Pin(terminalSymbol, "Down");
        var expected = new List<string>();
        for (int i = 1; i <= 3; i++)
        {
            double x = 40 + i * 60;
            Assert.Equal(up[0].GetDouble(), down[0].GetDouble());
            foreach ((string direction, JsonElement target, double offset, string end) in new[] { ("Down", up, 40d, "T"), ("Up", down, -40d, "B") })
            {
                JsonElement pin = Pin(endpointSymbol, direction);
                string tag = "=GAA+A1-ET" + suffix + end + i.ToString(CultureInfo.InvariantCulture);
                await eplan.CallAsync("eplan_device_place", new()
                {
                    ["project"] = project,
                    ["page"] = page,
                    ["library"] = "IEC_symbol",
                    ["symbol"] = "AS",
                    ["symbol_variant"] = 0,
                    ["tag"] = tag,
                    ["x"] = x + target[0].GetDouble() - pin[0].GetDouble(),
                    ["y"] = 140 + target[1].GetDouble() + offset - pin[1].GetDouble()
                });
                expected.Add(string.Join("|", new[] { strip + ":" + i.ToString(CultureInfo.InvariantCulture), tag }.Order(StringComparer.Ordinal)));
            }
        }
        var generate = new Dictionary<string, object?> { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(new[] { page }) };
        await eplan.CallAsync("eplan_connection_generate", generate);
        var snapshots = new List<string> { (await selection.SnapshotAsync()).Hash };
        Dictionary<string, object?> Args(int number) => new()
        {
            ["project"] = project,
            ["page"] = page,
            ["terminal_strip"] = strip,
            ["terminal_number"] = number.ToString(CultureInfo.InvariantCulture),
            ["symbol_variant"] = 0,
            ["x"] = 40 + number * 60,
            ["y"] = 140
        };
        string shown = strip;
        for (int i = 1; i <= 3; i++)
        {
            Dictionary<string, object?> args = Args(i);
            if (i == 1)
            { args["terminal_strip_text"] = "操作控制盒"; }
            JsonElement preview = await eplan.CallAsync("eplan_device_place", new(args) { ["dry_run"] = true });
            Assert.Equal(snapshots[^1], (await selection.SnapshotAsync()).Hash);
            JsonElement placed = await eplan.CallAsync("eplan_device_place", args);
            JsonElement terminal = placed.GetProperty("terminal");
            Assert.Equal(("committed", "AI: eplan_device_place"), (placed.GetProperty("execution").GetString(), placed.GetProperty("undo").GetString()));
            // Live T7c: this project's structure settings drop the higher-level function (=GAA) from strip names.
            shown = terminal.GetProperty("strip").GetString()!;
            Assert.True(strip == shown || strip.EndsWith(shown, StringComparison.Ordinal), shown);
            Assert.Equal(i.ToString(CultureInfo.InvariantCulture), terminal.GetProperty("number").GetString());
            Assert.True(terminal.GetProperty("owner").ValueKind == JsonValueKind.Null || terminal.GetProperty("owner").GetString() == shown); // relation may be pending until commit
            Assert.Equal([40d + i * 60, 140d], terminal.GetProperty("position").EnumerateArray().Select(p => p.GetDouble()));
            Assert.Equal(i == 1, terminal.GetProperty("stripCreated").GetBoolean());
            Assert.Equal(JsonValueKind.Null, preview.GetProperty("terminal").GetProperty("sortCode").ValueKind);
            TestContext.Current.TestOutputHelper!.WriteLine($"Terminal {i}: assigned sort code {terminal.GetProperty("sortCode")}");
            snapshots.Add((await selection.SnapshotAsync()).Hash);
        }
        JsonElement members = await eplan.CallAsync("eplan_terminal_strip_get", new() { ["project"] = project, ["strip"] = shown });
        Assert.Equal(["1", "2", "3"], members.GetProperty("terminals").EnumerateArray().Select(t => t.GetProperty("designation").GetString()));
        JsonElement duplicate = await eplan.ErrorAsync("eplan_device_place", new(Args(3)) { ["terminal_strip"] = strip.ToLowerInvariant() });
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (duplicate.GetProperty("reason").GetString(), duplicate.GetProperty("execution").GetString()));
        Assert.Equal(snapshots[^1], (await selection.SnapshotAsync()).Hash);
        await eplan.CallAsync("eplan_connection_generate", generate); // A52: a separate commit from all placements.
        JsonElement connections = await eplan.CallAsync("eplan_connection_list", new() { ["project"] = project, ["page"] = page, ["limit"] = 200 });
        string[] actual = connections.GetProperty("connections").EnumerateArray().Select(c => string.Join("|", new[]
        {
            c.GetProperty("from").GetProperty("device").GetString(), c.GetProperty("to").GetProperty("device").GetString()
        }.Order(StringComparer.Ordinal))).ToArray();
        // EPLAN names the terminals after the strip as stored (shown), which may lack the leading =GAA.
        string[] wanted = [.. expected.Select(e => string.Join("|", e.Split('|').Select(p => p.StartsWith(strip + ":", StringComparison.Ordinal) ? shown + p[strip.Length..] : p).Order(StringComparer.Ordinal)))];
        Assert.Equal(wanted.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        await selection.UndoAsync(); // Undo only generation; all three terminals remain.
        Assert.Equal(snapshots[3], (await selection.SnapshotAsync()).Hash);
        for (int i = 2; i >= 0; i--)
        {
            await selection.UndoAsync();
            Assert.Equal(snapshots[i], (await selection.SnapshotAsync()).Hash);
        }
        JsonElement strips = await eplan.CallAsync("eplan_terminal_strip_list", new() { ["project"] = project, ["name_contains"] = strip, ["limit"] = 200 });
        Assert.Empty(strips.GetProperty("strips").EnumerateArray());
    }
}

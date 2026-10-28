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
        async Task<JsonElement> Symbol(string name) => Assert.Single((await eplan.CallAsync("eplan_symbol_search", new()
        {
            ["project"] = project,
            ["library"] = "IEC_symbol",
            ["name_contains"] = name,
            ["limit"] = 200
        })).GetProperty("symbols").EnumerateArray(), s => s.GetProperty("name").GetString() == name && s.GetProperty("variant").GetInt32() == 0);
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
            Assert.Equal((strip, strip, i.ToString(CultureInfo.InvariantCulture)), (terminal.GetProperty("strip").GetString(), terminal.GetProperty("owner").GetString(), terminal.GetProperty("number").GetString()));
            Assert.Equal([40d + i * 60, 140d], terminal.GetProperty("position").EnumerateArray().Select(p => p.GetDouble()));
            Assert.Equal(i == 1, terminal.GetProperty("stripCreated").GetBoolean());
            Assert.Equal(JsonValueKind.Null, preview.GetProperty("terminal").GetProperty("sortCode").ValueKind);
            TestContext.Current.TestOutputHelper!.WriteLine($"Terminal {i}: assigned sort code {terminal.GetProperty("sortCode")}");
            snapshots.Add((await selection.SnapshotAsync()).Hash);
        }
        JsonElement members = await eplan.CallAsync("eplan_terminal_strip_get", new() { ["project"] = project, ["strip"] = strip });
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
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
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

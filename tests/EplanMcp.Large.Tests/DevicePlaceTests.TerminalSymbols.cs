using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task ExplicitTerminalSymbols_ReadsNativeLibraryVariantAndNumbers_EachCallUndoesOnce()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project;
        string page = await NewPageAsync(project);
        string strip = "-XT9" + page[Prefix.Length..];
        string shown = strip;
        var snapshots = new List<string> { (await session.SnapshotAsync()).Hash };
        try
        {
            foreach ((string library, string symbol, string number, int x) in new[] { ("IEC_symbol", "X2_2", "1", 80), ("GRAPHICS", "KA1_PE", "2", 140) })
            {
                JsonElement result = await PartsCallAsync("eplan_device_place", new()
                {
                    ["project"] = project,
                    ["page"] = page,
                    ["terminal_strip"] = strip,
                    ["terminal_number"] = number,
                    ["library"] = library,
                    ["symbol"] = symbol,
                    ["symbol_variant"] = 0,
                    ["x"] = x,
                    ["y"] = 160,
                    ["dry_run"] = false
                });
                Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
                JsonElement placedSymbol = result.GetProperty("symbol");
                Assert.Equal((library, symbol, 0), (placedSymbol.GetProperty("library").GetString(), placedSymbol.GetProperty("name").GetString(), placedSymbol.GetProperty("variant").GetInt32()));
                Assert.Equal("Terminal", placedSymbol.GetProperty("functionCategory").GetString());
                JsonElement terminal = result.GetProperty("terminal");
                shown = terminal.GetProperty("strip").GetString()!;
                Assert.Equal(number, terminal.GetProperty("number").GetString());
                Assert.Equal(shown + ":" + number, result.GetProperty("device").GetProperty("tag").GetString());
                JsonElement members = await PartsCallAsync("eplan_terminal_strip_get", new() { ["project"] = project, ["strip"] = shown });
                Assert.Single(members.GetProperty("terminals").EnumerateArray(), t => t.GetProperty("designation").GetString() == number);
                snapshots.Add((await session.SnapshotAsync()).Hash);
            }
            JsonElement read = await PartsCallAsync("eplan_terminal_strip_get", new() { ["project"] = project, ["strip"] = shown });
            Assert.Equal(["1", "2"], read.GetProperty("terminals").EnumerateArray().Select(t => t.GetProperty("designation").GetString()));
            for (int i = snapshots.Count - 2; i >= 0; i--)
            {
                await session.UndoAsync();
                Assert.Equal(snapshots[i], (await session.SnapshotAsync()).Hash);
            }
        }
        catch
        {
            try
            { await PartsCallAsync("eplan_terminal_strip_get", new() { ["project"] = project, ["strip"] = shown }); }
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("Terminal strip readback failed: " + error); }
            try
            { await PartsCallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200 }); }
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("Terminal page readback failed: " + error); }
            throw;
        }
    }
}

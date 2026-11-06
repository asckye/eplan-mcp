using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    private static readonly double[] _partStripPosition = [60.0, 160.0];

    [Fact]
    public async Task TerminalFromPart_CreatesItsStripWithTextAndPosition_NextJoins_UndoRestores()
    {
        // A13: the strip definition cannot be created in a SafetyPoint; this route runs in a Transaction.
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project;
        string page = await NewPageAsync(project);
        string strip = "-XP7" + page[Prefix.Length..];
        string before = (await session.SnapshotAsync()).Hash;
        JsonElement first = await PartsCallAsync("eplan_device_place", new()
        {
            ["project"] = project,
            ["page"] = page,
            ["part"] = "PXC.3211813",
            ["library"] = "IEC_symbol",
            ["symbol"] = "X2_2",
            ["symbol_variant"] = 0,
            ["terminal_strip"] = strip,
            ["terminal_number"] = "1",
            ["terminal_strip_text"] = "zh_CN@电源;en_US@Power;",
            ["terminal_strip_position"] = _partStripPosition,
            ["x"] = 80,
            ["y"] = 160,
            ["dry_run"] = false
        });
        Assert.Equal(ExecutionState.Committed, first.GetProperty("execution").GetString());
        JsonElement next = await PartsCallAsync("eplan_device_place", new()
        {
            ["project"] = project,
            ["page"] = page,
            ["part"] = "PXC.3211813",
            ["library"] = "IEC_symbol",
            ["symbol"] = "X2_2",
            ["symbol_variant"] = 0,
            ["terminal_strip"] = strip,
            ["terminal_number"] = "2",
            ["x"] = 88,
            ["y"] = 160,
            ["dry_run"] = false
        });
        Assert.Equal(ExecutionState.Committed, next.GetProperty("execution").GetString());
        JsonElement read = await eplan.CallAsync("eplan_terminal_strip_get", new() { ["project"] = project, ["strip"] = strip });
        Assert.Equal(2, read.GetProperty("terminals").GetArrayLength());
        await session.UndoAsync();
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);
    }

    [Fact]
    public async Task ExistingNonMainTerminal_AssignsPart_ReadsArticleReferences_OneUndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project;
        string page = await NewPageAsync(project);
        string tag = "";
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build EplanMcp.Large.ConnectionHelper before running this test.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            JsonElement placed = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "IEC_symbol",
                ["symbol"] = "X2_2",
                ["symbol_variant"] = 0,
                ["terminal_strip"] = "-XR6" + page[Prefix.Length..],
                ["terminal_number"] = "1",
                ["x"] = 80,
                ["y"] = 160,
                ["dry_run"] = false
            });
            tag = placed.GetProperty("device").GetProperty("tag").GetString()!;
            JsonElement original = await ReadTerminalPartsAsync(project, page, tag);
            Assert.False(original.GetProperty("main").GetBoolean());
            Assert.Empty(original.GetProperty("parts").EnumerateArray());
            string before = (await session.SnapshotAsync()).Hash;
            JsonElement assigned = await PartsCallAsync("eplan_device_update", new()
            { ["project"] = project, ["tag"] = tag, ["part"] = "PXC.3211813", ["dry_run"] = false });
            Assert.Equal(ExecutionState.Committed, assigned.GetProperty("execution").GetString());
            JsonElement read = await ReadTerminalPartsAsync(project, page, tag);
            JsonElement part = Assert.Single(read.GetProperty("parts").EnumerateArray());
            Assert.Equal(("PXC.3211813", "1", 1), (part.GetProperty("part").GetString(), part.GetProperty("variant").GetString(), part.GetProperty("count").GetInt32()));
            Assert.Equal(tag, read.GetProperty("name").GetString());
            Assert.Equal("Terminal", read.GetProperty("category").GetString());
            Assert.False(read.GetProperty("main").GetBoolean());
            // A93: a second part needs the SafetyPoint scope; the data transfer that follows has its own undo step.
            await PartsCallAsync("eplan_device_update", new() { ["project"] = project, ["tag"] = tag, ["part"] = "PXC.3022218", ["dry_run"] = false });
            Assert.Equal(2, (await ReadTerminalPartsAsync(project, page, tag)).GetProperty("parts").GetArrayLength());
            JsonElement updated = await PartsCallAsync("eplan_device_update", new() { ["project"] = project, ["tag"] = tag, ["update_from_parts"] = true, ["dry_run"] = false });
            Assert.Equal(ExecutionState.Committed, updated.GetProperty("execution").GetString());
            Assert.Equal(2, (await ReadTerminalPartsAsync(project, page, tag)).GetProperty("parts").GetArrayLength());
            await session.UndoAsync();
            Assert.Equal(2, (await ReadTerminalPartsAsync(project, page, tag)).GetProperty("parts").GetArrayLength());
            await session.UndoAsync();
            await session.UndoAsync();
            Assert.Equal(original.GetRawText(), (await ReadTerminalPartsAsync(project, page, tag)).GetRawText());
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        }
        catch
        {
            try
            { await ReadTerminalPartsAsync(project, page, tag); }
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("Terminal readback failed: " + error); }
            throw;
        }
        finally
        { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }

    private async Task<JsonElement> ReadTerminalPartsAsync(string project, string page, string tag)
    {
        string file = Path.Combine(host.Home, "terminal-parts-" + Guid.NewGuid().ToString("N") + ".json");
        string request = JsonSerializer.Serialize(new { project, page, tag });
        TestContext.Current.TestOutputHelper!.WriteLine("Terminal ArticleReferences request: " + request);
        await File.WriteAllTextAsync(file, request, TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeTerminalPartReadback /REQUEST:\"" + file + "\"");
        string json = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper.WriteLine("Terminal ArticleReferences result/readback: " + json);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), json);
        return document.RootElement.Clone();
    }
}

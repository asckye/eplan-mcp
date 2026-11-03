using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    [Trait("Difference", "D6")]
    public async Task StripDefinition_RequestedPositionAndFunctionText_PrintPageDisplay_OneUndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = await NewPageAsync(session.Project);
        string tag = "-XD6" + page[Prefix.Length..];
        async Task<JsonElement> Drawings() => (await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" })).GetProperty("drawings");
        JsonElement before = await Drawings();
        string business = (await session.SnapshotAsync()).Hash;
        var args = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["terminal_strip"] = tag,
            ["terminal_number"] = "1",
            ["terminal_strip_text"] = "Fictional control box",
            ["terminal_strip_position"] = new[] { 72d, 120d },
            ["x"] = 100,
            ["y"] = 160
        };
        JsonElement preview = await eplan.CallAsync("eplan_device_place", new(args) { ["dry_run"] = true });
        Assert.Equal(before.GetRawText(), (await Drawings()).GetRawText());
        JsonElement result = await eplan.CallAsync("eplan_device_place", new(args) { ["dry_run"] = false });
        TestContext.Current.TestOutputHelper!.WriteLine("D6 placement: " + result);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(preview.GetProperty("terminal").GetProperty("stripPosition").GetRawText(), result.GetProperty("terminal").GetProperty("stripPosition").GetRawText());
        JsonElement read = await Drawings();
        TestContext.Current.TestOutputHelper.WriteLine("D6 EPLAN definition and displayed property texts: " + read);
        Assert.False(read.GetProperty("truncated").GetBoolean());
        JsonElement strip = Assert.Single(read.GetProperty("items").EnumerateArray(), p => p.GetProperty("kind").GetString() == "TerminalStrip");
        Assert.Equal([72d, 120d], strip.GetProperty("location").EnumerateArray().Select(n => n.GetDouble()));
        string full = result.GetProperty("terminal").GetProperty("strip").GetString()!;
        JsonElement device = await eplan.CallAsync("eplan_device_get", new() { ["project"] = session.Project, ["tag"] = full, ["language"] = "??_??" });
        Assert.Equal("Fictional control box", device.GetProperty("device").GetProperty("text").GetString());
        string displayed = string.Join("\n", read.GetProperty("items").EnumerateArray().Where(p => p.GetProperty("kind").GetString() == "PropertyPlacement"
            && p.TryGetProperty("parent", out JsonElement owner) && owner.ValueKind == JsonValueKind.Object && owner.GetProperty("oid").GetInt64() == strip.GetProperty("locator").GetProperty("oid").GetInt64())
            .Select(p => p.GetProperty("properties").GetProperty("display").GetString()));
        TestContext.Current.TestOutputHelper.WriteLine("D6 page shows: " + displayed);
        Assert.Contains(tag, displayed, StringComparison.Ordinal);
        Assert.Contains("Fictional control box", displayed, StringComparison.Ordinal);
        TestContext.Current.TestOutputHelper.WriteLine("D6 PNG: " + await eplan.CallAsync("eplan_export_image", new() { ["project"] = session.Project, ["pages"] = new[] { page } }));
        await session.UndoAsync();
        Assert.Equal(before.GetRawText(), (await Drawings()).GetRawText());
        Assert.Equal(business, (await session.SnapshotAsync()).Hash);
    }
}

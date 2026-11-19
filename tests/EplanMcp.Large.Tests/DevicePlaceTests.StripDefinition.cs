using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task UnplacedStripDefinition_IsDeletedByTag_UndoRestores()
    {
        const string Delete = "eplan_placement_delete";
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        await using var destructive = new DestructiveHost();
        await destructive.InitializeAsync();
        Assert.Equal(destructive.Writable, session.Project, ignoreCase: true);
        string page = await NewPageAsync(session.Project);
        string suffix = page[Prefix.Length..];
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine("H0 after page creation: " + (await session.SnapshotAsync()).Hash);

        async Task<JsonElement> Place(string label, double x, bool placeDefinition = false)
        {
            var args = new Dictionary<string, object?>
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["terminal_strip"] = label + suffix,
                ["terminal_number"] = "1",
                ["x"] = x,
                ["y"] = 160,
                ["dry_run"] = false
            };
            if (placeDefinition)
            {
                args["terminal_strip_text"] = "Placed definition";
                args["terminal_strip_position"] = new[] { 72d, 120d };
            }
            return await eplan.CallAsync("eplan_device_place", args);
        }
        Dictionary<string, object?> Device(string tag) => new() { ["project"] = session.Project, ["tag"] = tag };
        Dictionary<string, object?> ByTag(string tag) => new() { ["project"] = session.Project, ["unplaced"] = new[] { tag } };
        async Task<(bool Succeeded, JsonElement Result)> LogCall(string label, string tool, Dictionary<string, object?> args)
        {
            var outcome = await destructive.CallOrErrorAsync(tool, args);
            output.WriteLine($"{label} ({(outcome.Succeeded ? "result" : "error")}): {outcome.Result.GetRawText()}");
            return outcome;
        }
        void ApproveDefinition(string tag, int dependents)
        {
            string count = dependents == 0 ? "" : $" ({dependents} functions named {tag}:… exist)";
            destructive.Approvals.Approve(Delete, session.Project, [$"delete function {tag} (TerminalDefText, unplaced){count}"]);
        }

        JsonElement first = await Place("-XD81", 96);
        string strip = first.GetProperty("terminal").GetProperty("strip").GetString()!;
        output.WriteLine("H1 after first terminal: " + (await session.SnapshotAsync()).Hash);
        JsonElement device = (await eplan.CallAsync("eplan_device_get", Device(strip))).GetProperty("device");
        Assert.Equal(("TerminalDefText", JsonValueKind.Null), (device.GetProperty("category").GetString(), device.GetProperty("page").ValueKind));

        JsonElement placed = await Place("-XD82", 132, placeDefinition: true);
        string placedTag = placed.GetProperty("terminal").GetProperty("strip").GetString()!;
        JsonElement refusal = await destructive.ErrorAsync(Delete, ByTag(placedTag));
        Assert.Equal(ErrorReason.InvalidArgument, refusal.GetProperty("reason").GetString());
        string before = (await session.SnapshotAsync()).Hash;
        Dictionary<string, object?> deletion = ByTag(strip);
        JsonElement preview = await destructive.CallAsync(Delete, deletion);
        output.WriteLine("Unplaced preview: " + preview.GetRawText());
        Assert.Equal(strip, Assert.Single(preview.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(1, preview.GetProperty("dependents").GetInt32());
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);

        ApproveDefinition(strip, 1);
        deletion["dry_run"] = false;
        var deleted = await LogCall("Delete unplaced strip", Delete, deletion);
        await LogCall("Strip after deletion", "eplan_device_get", Device(strip));
        await LogCall("Page devices after deletion", "eplan_device_list",
            new() { ["project"] = session.Project, ["page"] = page, ["include_auxiliary"] = true, ["limit"] = 200 });
        Assert.Equal(ExecutionState.Committed, deleted.Result.GetProperty("execution").GetString());
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);

        JsonElement third = await Place("-XD83", 168);
        string thirdStrip = third.GetProperty("terminal").GetProperty("strip").GetString()!;
        string terminal = third.GetProperty("device").GetProperty("tag").GetString()!;
        string beforePageDelete = (await session.SnapshotAsync()).Hash;
        JsonElement drawings = (await eplan.CallAsync("eplan_page_get",
            new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" })).GetProperty("drawings");
        JsonElement target = drawings.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("kind").GetString() == "Terminal"
            && row.GetProperty("properties").GetProperty("name").GetString() == terminal);
        destructive.Approvals.Approve(Delete, session.Project,
            [$"delete function {terminal} (Terminal) (Removing the last placed function also deletes its device and part references.)"]);
        var terminalDelete = await LogCall("Delete third terminal from page", Delete,
            new() { ["project"] = session.Project, ["page"] = page, ["items"] = new[] { target.GetProperty("locator") }, ["dry_run"] = false });
        var empty = await LogCall("Third strip after terminal deletion", "eplan_device_get", Device(thirdStrip));
        Assert.Equal(ExecutionState.Committed, terminalDelete.Result.GetProperty("execution").GetString());
        if (empty.Succeeded && !empty.Result.GetProperty("truncated").GetBoolean()
            && empty.Result.GetProperty("device").GetProperty("category").GetString() == "TerminalDefText"
            && empty.Result.GetProperty("device").GetProperty("page").ValueKind == JsonValueKind.Null
            && empty.Result.GetProperty("functions").GetArrayLength() == 0)
        {
            Dictionary<string, object?> emptyDelete = ByTag(thirdStrip);
            JsonElement emptyPreview = await destructive.CallAsync(Delete, emptyDelete);
            output.WriteLine("Third strip preview: " + emptyPreview.GetRawText());
            ApproveDefinition(thirdStrip, emptyPreview.GetProperty("dependents").GetInt32());
            emptyDelete["dry_run"] = false;
            var emptyResult = await LogCall("Delete empty strip", Delete, emptyDelete);
            await LogCall("Third strip after definition deletion", "eplan_device_get", Device(thirdStrip));
            Assert.Equal(ExecutionState.Committed, emptyResult.Result.GetProperty("execution").GetString());
            await session.UndoAsync();
        }
        await session.UndoAsync();
        Assert.Equal(beforePageDelete, (await session.SnapshotAsync()).Hash);
    }

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

    /// <summary>
    /// A161: the description of a terminal strip is the function text of its definition. A strip whose terminals were
    /// placed without asking for a definition has an unplaced one with no text; both are changed by eplan_property_set.
    /// </summary>
    [Fact]
    public async Task StripDefinitionText_PlacedAndUnplaced_IsChangedByPropertySet_UndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = await NewPageAsync(session.Project);
        string business = (await session.SnapshotAsync()).Hash;
        string placed = "-XD7" + page[Prefix.Length..], bare = "-XD8" + page[Prefix.Length..];
        JsonElement a = await eplan.CallAsync("eplan_device_place", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["terminal_strip"] = placed,
            ["terminal_number"] = "1",
            ["terminal_strip_text"] = "Old text",
            ["terminal_strip_position"] = (double[])[72d, 120d],
            ["x"] = 100,
            ["y"] = 160,
            ["dry_run"] = false
        });
        JsonElement b = await eplan.CallAsync("eplan_device_place", new() { ["project"] = session.Project, ["page"] = page, ["terminal_strip"] = bare, ["terminal_number"] = "1", ["x"] = 140, ["y"] = 160, ["dry_run"] = false });
        (string Tag, string Text)[] strips = [(a.GetProperty("terminal").GetProperty("strip").GetString()!, "New text"), (b.GetProperty("terminal").GetProperty("strip").GetString()!, "Spare strip")];
        async Task<JsonElement> Device(string tag) => (await eplan.CallAsync("eplan_device_get", new() { ["project"] = session.Project, ["tag"] = tag, ["language"] = "??_??" })).GetProperty("device");
        Assert.Equal(("TerminalDefText", "Old text"), ((await Device(strips[0].Tag)).GetProperty("category").GetString(), (await Device(strips[0].Tag)).GetProperty("text").GetString()));
        JsonElement unplaced = await Device(strips[1].Tag);
        Assert.Equal(("TerminalDefText", JsonValueKind.Null), (unplaced.GetProperty("category").GetString(), unplaced.GetProperty("page").ValueKind));
        foreach ((string tag, string text) in strips)
        {
            JsonElement set = await eplan.CallAsync("eplan_property_set", new()
            {
                ["project"] = session.Project,
                ["dry_run"] = false,
                ["changes"] = new[] { new Dictionary<string, object?> { ["target"] = new { device_tag = tag }, ["property"] = new { property_id = 20011 }, ["language"] = "??_??", ["value"] = text } }
            });
            TestContext.Current.TestOutputHelper!.WriteLine("Strip text set: " + set);
            Assert.Equal(ExecutionState.Committed, set.GetProperty("execution").GetString());
            Assert.Equal(text, (await Device(tag)).GetProperty("text").GetString());
        }
        JsonElement refused = await eplan.ErrorAsync("eplan_property_set", new()
        {
            ["project"] = session.Project,
            ["dry_run"] = true,
            ["changes"] = new[] { new Dictionary<string, object?> { ["target"] = new { device_tag = strips[0].Tag }, ["property"] = new { property_id = 20027 }, ["value"] = "x" } }
        });
        Assert.Equal("POLICY_DENIED", refused.GetProperty("reason").GetString());
        for (int i = 0; i < 4; i++)
        { await session.UndoAsync(); }
        Assert.Equal(business, (await session.SnapshotAsync()).Hash);
    }
}

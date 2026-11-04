using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementMoveTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task CoilTagAndFreeText_FollowMove_ConnectionsRegenerate_OneUndoRestoresOutsideObjects()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string tag = "-KM" + Guid.NewGuid().ToString("N")[..8];
        foreach ((string name, int y) in new[] { (tag, 160), (tag + "B", 80) })
        {
            await host.CallAsync("eplan_device_place", new()
            {
                ["dry_run"] = false,
                ["project"] = host.Writable,
                ["page"] = page,
                ["library"] = "IEC_symbol",
                ["symbol"] = "K",
                ["symbol_variant"] = 0,
                ["x"] = 80,
                ["y"] = y,
                ["tag"] = name,
                ["display_properties"] = new[] { new { property_id = 20214, offset_x_mm = 8, offset_y_mm = 8 } },
            });
        }
        await host.CallAsync("eplan_placement_create", new()
        {
            ["dry_run"] = false,
            ["project"] = host.Writable,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["items"] = new object[] { new { kind = "text", position = (int[])[140, 100], height_mm = 3.5, contents = new Dictionary<string, string> { ["??_??"] = "Move this note" } },
                new { kind = "rectangle", corners = (int[][])[[200, 200], [220, 220]] } },
        });
        var target = new Dictionary<string, object?> { ["project"] = host.Writable, ["pages"] = new[] { page } };
        await host.CallAsync("eplan_connection_generate", new(target) { ["dry_run"] = false });
        JsonElement connectionsBefore = await host.CallAsync("eplan_connection_list", new() { ["project"] = host.Writable, ["page"] = page });
        Assert.NotEmpty(connectionsBefore.GetProperty("connections").EnumerateArray());
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        PageDrawingGeometry before = await Read(bridge, page);
        string original = JsonSerializer.Serialize(before);
        string business = (await session.SnapshotAsync()).Hash;
        DrawingPlacement coil = Assert.Single(before.Placements, p => p.Kind == "Function" && p.Location.SequenceEqual([80d, 160d]));
        DrawingPlacement label = Assert.Single(before.Placements, p => p.Kind == "PropertyPlacement" && p.Properties.GetValueOrDefault("display") == tag);
        DrawingPlacement text = Assert.Single(before.Placements, p => p.Kind == "Text");
        var args = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["response_format"] = "detailed",
            ["moves"] = new[] { new { locator = coil.Locator, x_mm = 80, y_mm = 140 }, new { locator = text.Locator, x_mm = 160, y_mm = 100 } },
        };
        JsonElement preview = await host.CallAsync("eplan_placement_update", new(args) { ["dry_run"] = true });
        Assert.NotEmpty(preview.GetProperty("graphicalChanges").EnumerateArray());
        Assert.Equal(original, JsonSerializer.Serialize(await Read(bridge, page)));
        JsonElement moved = await host.CallAsync("eplan_placement_update", new(args) { ["dry_run"] = false });
        Assert.Equal(ExecutionState.Committed, moved.GetProperty("execution").GetString());
        PageDrawingGeometry after = await Read(bridge, page);
        DrawingPlacement Find(DrawingPlacement row) => Assert.Single(after.Placements, p => p.Kind == row.Kind && p.Locator.Type == row.Locator.Type && p.Locator.Oid == row.Locator.Oid && p.Properties.GetValueOrDefault("display") == row.Properties.GetValueOrDefault("display"));
        Assert.Equal([80d, 140d], Find(coil).Location);
        Assert.Equal([label.Location[0], label.Location[1] - 20], Find(label).Location);
        Assert.Equal([160d, 100d], Find(text).Location);
        // Several empty property texts share their function's identifier (A57), so compare everything outside the
        // closure as one ordered sequence instead of looking each row up.
        bool Outside(DrawingPlacement p) => p.Locator.Oid != coil.Locator.Oid && p.Locator.Oid != text.Locator.Oid && p.Parent?.Oid != coil.Locator.Oid;
        Assert.Equal(JsonSerializer.Serialize(before.Placements.Where(Outside)), JsonSerializer.Serialize(after.Placements.Where(Outside)));
        JsonElement generated = await host.CallAsync("eplan_connection_generate", new(target) { ["dry_run"] = false });
        // Alignment and endpoint identities are retained; regeneration must not leave an empty step (A19).
        Assert.Equal(ExecutionState.NotStarted, generated.GetProperty("execution").GetString());
        JsonElement connectionsAfter = await host.CallAsync("eplan_connection_list", new() { ["project"] = host.Writable, ["page"] = page });
        Assert.Equal(connectionsBefore.GetProperty("connections").GetRawText(), connectionsAfter.GetProperty("connections").GetRawText());
        await session.UndoAsync();
        PageDrawingGeometry undone = await Read(bridge, page);
        Assert.Equal(original, JsonSerializer.Serialize(undone));
        Assert.Equal(business, (await session.SnapshotAsync()).Hash);
    }

    [Fact]
    public async Task PropertyStyle_ReadsAllFieldsAndDocumentsHiddenStateException()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await host.CallAsync("eplan_device_place", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["dry_run"] = false,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["x"] = 80,
            ["y"] = 160,
            ["tag"] = "-STYLE",
            ["display_properties"] = new[] { new { property_id = 20214, offset_x_mm = 8, offset_y_mm = 8 } }
        });
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        PageDrawingGeometry before = await Read(bridge, page);
        DrawingPlacement label = Assert.Single(before.Placements, p => p.Kind == "PropertyPlacement" && p.Properties.GetValueOrDefault("display") == "-STYLE");
        var request = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["dry_run"] = false,
            ["snap_to_grid"] = false,
            ["response_format"] = "detailed",
            ["property_moves"] = new[] { new { parent = label.Parent, property_id = 20214, property_index = int.Parse(label.Properties["property_index"], System.Globalization.CultureInfo.InvariantCulture),
                x_mm = 120, y_mm = 160, angle_deg = 90, alignment = "BaseLineCenter", height_mm = 5, visible = false, layer = 108,
                pen = new { color = -16002, width_mm = 0.25, style = 1, pattern_length = 8, line_end = 1 } } }
        };
        TestContext.Current.TestOutputHelper?.WriteLine("request: " + JsonSerializer.Serialize(request));
        JsonElement result;
        try
        { result = await host.CallAsync("eplan_placement_update", request); }
        catch (Exception error) { TestContext.Current.TestOutputHelper?.WriteLine("result: " + error + "\nreadback: " + JsonSerializer.Serialize(await Read(bridge, page))); throw; }
        TestContext.Current.TestOutputHelper?.WriteLine("result: " + result);
        PageDrawingGeometry after = await Read(bridge, page);
        TestContext.Current.TestOutputHelper?.WriteLine("readback: " + JsonSerializer.Serialize(after));
        DrawingPlacement actual = Assert.Single(after.Placements, p => p.Kind == "PropertyPlacement" && p.Properties.GetValueOrDefault("display") == "-STYLE");
        Assert.Equal([120d, 160d], actual.Location);
        Assert.Equal("90", actual.Properties["angle_deg"]);
        Assert.Equal("BaseLineCenter", actual.Properties["alignment"]);
        Assert.Equal("5", actual.Properties["height_mm"]);
        Assert.Equal("108", actual.Properties["layer"]);
        foreach (var pair in new Dictionary<string, string> { ["color"] = "-16002", ["width_mm"] = "0.25", ["style"] = "1", ["pattern_length"] = "8", ["line_end"] = "1" })
        { Assert.Equal(pair.Value, actual.Properties["pen_" + pair.Key]); }
        Assert.Contains("not compared", result.GetProperty("warning").GetString(), StringComparison.Ordinal);
        await session.UndoAsync();
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Read(bridge, page)));
    }

    private Task<PageDrawingGeometry> Read(BridgeClient bridge, string page) => bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
        new() { Project = host.Writable, Page = page }, TestContext.Current.CancellationToken);
}

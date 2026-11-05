using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class GraphicEditTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly int[] _origin = [0, 0];
    [Theory]
    [InlineData("scale")]
    [InlineData("order")]
    [InlineData("group")]
    [InlineData("ungroup")]
    [InlineData("explode_block")]
    [InlineData("dock_text")]
    [InlineData("undock_text")]
    [InlineData("corner_radius")]
    [InlineData("copy_format")]
    [InlineData("cut_off")]
    [InlineData("set_length")]
    [InlineData("stretch")]
    [InlineData("snap_to_grid")]
    [InlineData("chamfer")]
    [InlineData("fillet")]
    public async Task EachOperation_ReadsBackAndOneUndoRestoresTheNewPage(string operation)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        await Create(page,
            new { kind = "line", points = (double[][])[[40, 40], [100, 40]], pen = new { color = 1, width_mm = 0.25 }, layer = 108 },
            new { kind = "line", points = (double[][])[[70, 20], [70, 60]] },
            new { kind = "rectangle", corners = (double[][])[[120, 40], [160, 80]] },
            new { kind = "text", position = (double[])[200, 100], height_mm = 3.5, contents = new Dictionary<string, string> { ["??_??"] = "Graphic first" } },
            new { kind = "text", position = (double[])[200, 120], height_mm = 3.5, contents = new Dictionary<string, string> { ["??_??"] = "Graphic target" } },
            new { kind = "line", points = (double[][])[[250, 40], [270, 40]] },
            new { kind = "line", points = (double[][])[[270, 40], [270, 60]] },
            new { kind = "line", points = (double[][])[[180.31, 40.31], [190.31, 45.31]] });
        JsonElement[] lines = await Find(page, "Line"), texts = await Find(page, "Text");
        JsonElement rectangle = Assert.Single(await Find(page, "Rectangle"));
        JsonElement[] selected = [rectangle];
        var args = new Dictionary<string, object?> { ["project"] = host.Writable, ["page"] = page, ["operation"] = operation, ["response_format"] = "detailed" };
        switch (operation)
        {
            case "scale":
                args["scale_x"] = 2;
                args["scale_y"] = 0.5;
                args["origin_mm"] = new[] { 120, 40 };
                break;
            case "order":
                args["to"] = "front";
                break;
            case "corner_radius":
                args["radius_mm"] = 3;
                break;
            case "copy_format":
                args["source"] = lines[0];
                break;
            case "cut_off":
                selected = [lines[0]];
                args["point_mm"] = new[] { 55, 40 };
                break;
            case "set_length":
                selected = [lines[0]];
                args["length_mm"] = 45;
                args["anchor"] = "start";
                break;
            case "stretch":
                args["region_mm"] = new[] { 119, 39, 161, 41 };
                args["offset_mm"] = new[] { 5, 3 };
                break;
            case "snap_to_grid":
                selected = [lines[4]];
                break;
            case "chamfer":
                selected = [lines[2], lines[3]];
                args["distance_mm"] = 5;
                break;
            case "fillet":
                selected = [lines[2], lines[3]];
                args["radius_mm"] = 5;
                break;
            case "dock_text":
                selected = texts;
                args["dock_direction"] = "bottom";
                break;
            case "undock_text":
                await Edit(page, "dock_text", texts);
                selected = [Assert.Single((await Find(page, "Text")).Take(1))];
                break;
            case "group":
                await PlaceFunction(page);
                selected = [Assert.Single(await Find(page, "Function")), rectangle];
                break;
            case "ungroup":
                await Edit(page, "group", [lines[2], lines[3]]);
                selected = [Assert.Single(await Find(page, "Group"))];
                break;
            case "explode_block":
                await Create(page, new { kind = "block", name = "graphic_edit_block", members = await DrawingLocators(page, [lines[2], lines[3]]) });
                selected = [Assert.Single(await Find(page, "Block"))];
                break;
        }
        args["locators"] = selected;
        string before = await Hash();
        string? metadataBefore = operation is "dock_text" or "undock_text" or "corner_radius" ? await MetadataState() : null;
        string projectBefore = (await session.SnapshotAsync()).Hash;
        JsonElement preview = await Call("eplan_graphic_edit", args);
        Assert.Equal(ExecutionState.NotStarted, preview.GetProperty("execution").GetString());
        Assert.True(preview.GetProperty("dryRun").GetBoolean());
        Assert.Equal(before, await Hash());
        Assert.Equal(projectBefore, (await session.SnapshotAsync()).Hash);
        args["dry_run"] = false;
        JsonElement result = await Call("eplan_graphic_edit", args);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.False(result.GetProperty("recovered").GetBoolean());
        if (metadataBefore is null)
        { Assert.NotEqual(before, await Hash()); }
        else
        { Assert.NotEqual(metadataBefore, await MetadataState()); }
        JsonElement after = await Call("eplan_page_get", new() { ["project"] = host.Writable, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" });
        Evidence("readback", after);
        Verify(operation, result, after, rectangle);
        if (operation == "group")
        { Assert.Single(await Find(page, "Group")); }
        if (operation == "ungroup")
        { Assert.Empty(await Find(page, "Group")); }
        await session.UndoAsync();
        Assert.Equal(before, await Hash());
        if (metadataBefore is not null)
        { Assert.Equal(metadataBefore, await MetadataState()); }
        Assert.Equal(projectBefore, (await session.SnapshotAsync()).Hash);

        async Task<string> Hash() => SnapshotCanonical.Sha256(JsonSerializer.Serialize(await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = host.Writable, Page = page }, TestContext.Current.CancellationToken)));
        async Task<string> MetadataState()
        {
            JsonElement state = await Call("eplan_graphic_edit", new()
            {
                ["project"] = host.Writable,
                ["page"] = page,
                ["operation"] = "scale",
                ["locators"] = await Find(page, operation == "corner_radius" ? "Rectangle" : "Text"),
                ["scale_x"] = 1,
                ["scale_y"] = 1,
                ["origin_mm"] = _origin,
            });
            return JsonSerializer.Serialize(state.GetProperty("predicted").EnumerateArray().Select(p => new
            {
                docked = p.GetProperty("docked").GetRawText(),
                direction = p.GetProperty("dockingDirection").GetRawText(),
                chamfers = p.GetProperty("hasChamfers").GetRawText(),
                radius = p.GetProperty("cornerRadius").GetRawText(),
            }));
        }
    }

    [Fact]
    public async Task BlockContainingFunction_IsRejectedBeforeWriting()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await Create(page, new { kind = "line", points = (double[][])[[40, 40], [100, 40]] });
        await PlaceFunction(page);
        JsonElement[] members = await DrawingLocators(page, [Assert.Single(await Find(page, "Line")), Assert.Single(await Find(page, "Function"))]);
        string before = (await session.SnapshotAsync()).Hash;
        JsonElement error = await host.ErrorAsync("eplan_placement_create", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["dry_run"] = false,
            ["items"] = new[] { new { kind = "block", name = "must_not_drop_function", members } },
        });
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        Assert.Contains("Function", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);
    }

    private static readonly int[] _scaleOrigin = [40, 40];

    [Fact]
    public async Task Scale_LineTextAndGroup_ReadsBackAndOneUndoRestoresTheScale()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await Create(page,
            new { kind = "line", points = (double[][])[[40, 40], [100, 40]] },
            new { kind = "line", points = (double[][])[[40, 80], [100, 80]] },
            new { kind = "rectangle", corners = (double[][])[[120, 40], [160, 80]] },
            new { kind = "text", position = (double[])[200, 100], height_mm = 3.5, contents = new Dictionary<string, string> { ["??_??"] = "Graphic scale" } });
        JsonElement[] lines = await Find(page, "Line"), texts = await Find(page, "Text");
        JsonElement rectangle = Assert.Single(await Find(page, "Rectangle"));
        JsonElement group = (await Edit(page, "group", [lines[1], rectangle])).GetProperty("newLocators")[0].Clone();
        string before = (await session.SnapshotAsync(page)).Hash;
        await Call("eplan_graphic_edit", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["operation"] = "scale",
            ["locators"] = new[] { lines[0], texts[0], group },
            ["scale_x"] = 1.5,
            ["scale_y"] = 1.5,
            ["origin_mm"] = _scaleOrigin,
            ["layout_guard"] = "warn",
            ["dry_run"] = false,
        });
        JsonElement after = await Call("eplan_page_get", new() { ["project"] = host.Writable, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" });
        JsonElement[] rows = [.. after.GetProperty("drawings").GetProperty("items").EnumerateArray()];
        JsonElement Row(JsonElement locator) => Assert.Single(rows, r => SameId(r.GetProperty("locator"), locator));
        Assert.Equal([40d, 40d], Values(Row(lines[0]).GetProperty("location")));
        Assert.Equal([40d, 100d], Values(Row(lines[1]).GetProperty("location")));
        Assert.Equal([160d, 40d, 220d, 100d], Values(Row(rectangle).GetProperty("bounds")));
        Assert.Equal([280d, 130d], Values(Row(texts[0]).GetProperty("location")));
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
    }

    private static void Verify(string operation, JsonElement result, JsonElement page, JsonElement rectangle)
    {
        JsonElement[] rows = page.GetProperty("drawings").GetProperty("items").EnumerateArray().ToArray();
        JsonElement[] predicted = result.GetProperty("predicted").EnumerateArray().ToArray();
        Assert.NotEmpty(predicted);
        JsonElement Rect() => Assert.Single(rows, p => p.GetProperty("kind").GetString() == "Rectangle");
        switch (operation)
        {
            case "scale":
                Assert.Equal([120d, 40d, 200d, 60d], Values(Rect().GetProperty("bounds")));
                break;
            case "stretch":
                Assert.Equal([120d, 43d, 160d, 80d], Values(Rect().GetProperty("bounds")));
                break;
            case "order":
                Assert.Equal(65, Assert.Single(rows, r => SameId(r.GetProperty("locator"), rectangle)).GetProperty("drawingOrder").GetInt32());
                break;
            case "group":
                Assert.NotEmpty(result.GetProperty("newLocators").EnumerateArray());
                Assert.Single(predicted, r => r.GetProperty("kind").GetString() == "Group");
                break;
            case "ungroup":
                Assert.DoesNotContain(predicted, r => r.GetProperty("kind").GetString() == "Group");
                break;
            case "explode_block":
                Assert.DoesNotContain(rows, r => r.GetProperty("kind").GetString() == "Block");
                Assert.Equal(2, result.GetProperty("newLocators").GetArrayLength());
                break;
            case "copy_format":
                Assert.Equal("1", Rect().GetProperty("properties").GetProperty("pen_color").GetString());
                Assert.Equal("108", Rect().GetProperty("properties").GetProperty("layer").GetString());
                break;
            case "chamfer":
                Assert.Single(result.GetProperty("newLocators").EnumerateArray());
                Assert.Equal(6, rows.Count(r => r.GetProperty("kind").GetString() == "Line"));
                break;
            case "fillet":
                Assert.Single(result.GetProperty("newLocators").EnumerateArray());
                Assert.Single(rows, r => r.GetProperty("kind").GetString() == "Arc");
                break;
            case "dock_text":
                Assert.True(predicted[0].GetProperty("docked").GetBoolean());
                break;
            case "undock_text":
                Assert.False(predicted[0].GetProperty("docked").GetBoolean());
                break;
            case "corner_radius":
                Assert.Equal(3, predicted[0].GetProperty("cornerRadius").GetDouble());
                break;
            case "set_length":
                Assert.Equal([85d, 40d], Values(predicted[0].GetProperty("points")[1]));
                break;
            case "cut_off":
                Assert.Equal([70d, 40d], Values(predicted[0].GetProperty("points")[0]));
                break;
            case "snap_to_grid":
                Assert.False(result.GetProperty("noOp").GetBoolean());
                break;
        }
    }

    private Task<JsonElement> Create(string page, params object[] items) => Call("eplan_placement_create", new()
    { ["project"] = host.Writable, ["page"] = page, ["items"] = items, ["dry_run"] = false, ["snap_to_grid"] = false, ["layout_guard"] = "warn" });
    private Task<JsonElement> Edit(string page, string operation, JsonElement[] locators) => Call("eplan_graphic_edit", new()
    { ["project"] = host.Writable, ["page"] = page, ["operation"] = operation, ["locators"] = locators, ["dry_run"] = false });
    private Task<JsonElement> PlaceFunction(string page) => Call("eplan_device_place", new()
    {
        ["project"] = host.Writable,
        ["page"] = page,
        ["part"] = "SIE.3VA1032-2ED32-0AA0",
        ["library"] = "IEC_symbol",
        ["symbol"] = "Q_3LI",
        ["symbol_variant"] = 0,
        ["x"] = 52,
        ["y"] = 160,
        ["tag"] = "-QGRAPHIC" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
        ["dry_run"] = false,
    });
    private async Task<JsonElement[]> Find(string page, string kind)
    {
        JsonElement result = await Call("eplan_object_find", new() { ["project"] = host.Writable, ["page"] = page, ["object_type"] = kind, ["limit"] = 200 });
        return [.. result.GetProperty("items").EnumerateArray().OrderBy(p => p.GetProperty("locator").GetProperty("oid").GetInt64()).Select(p => p.GetProperty("locator").Clone())];
    }
    private async Task<JsonElement[]> DrawingLocators(string page, JsonElement[] targets)
    {
        JsonElement result = await Call("eplan_page_get", new()
        { ["project"] = host.Writable, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" });
        JsonElement[] roots = [.. result.GetProperty("drawings").GetProperty("items").EnumerateArray()
            .Where(row => !row.TryGetProperty("parent", out JsonElement parent) || parent.ValueKind == JsonValueKind.Null)];
        return [.. targets.Select(target => Assert.Single(roots, row => SameId(row.GetProperty("locator"), target)).GetProperty("locator").Clone())];
    }
    private async Task<JsonElement> Call(string tool, Dictionary<string, object?> args)
    {
        Evidence("request", new { tool, args });
        JsonElement result = await host.CallAsync(tool, args);
        Evidence("result", result);
        return result;
    }
    private static double[] Values(JsonElement value) => [.. value.EnumerateArray().Select(p => p.GetDouble())];
    private static bool SameId(JsonElement a, JsonElement b) => a.GetProperty("type").GetInt32() == b.GetProperty("type").GetInt32() && a.GetProperty("oid").GetInt64() == b.GetProperty("oid").GetInt64();
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}

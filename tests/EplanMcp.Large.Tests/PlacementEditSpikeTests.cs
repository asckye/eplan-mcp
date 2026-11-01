using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementEditSpikeTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    [Theory]
    [InlineData("copy", false)]
    [InlineData("delete", false)]
    [InlineData("rotate", false)]
    [InlineData("mirror", false)]
    [InlineData("copy", true)]
    [InlineData("delete", true)]
    [InlineData("rotate", true)]
    [InlineData("mirror", true)]
    [InlineData("mirror_horizontal", false)]
    public async Task NativeKinds_PrintCopyNamesDeleteClosurePivotsTextMirroringAndSingleUndo(string operation, bool rollback)
    {
        bool horizontal = operation == "mirror_horizontal";
        if (horizontal)
        { operation = "mirror"; }
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await Seed(page);
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper));
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            JsonElement seed = await Probe(new { project = session.Project, page, operation = "seed", oids = Array.Empty<long>() });
            Assert.True(seed.GetProperty("success").GetBoolean(), seed.ToString());
            string sentinel = Assert.Single(await session.CreatePagesAsync(1));
            JsonElement before = seed.GetProperty("after");
            long[] ids = before.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("oid").GetInt64()).ToArray();
            JsonElement result = await Probe(new { project = session.Project, page, operation, oids = ids, rollback, axis = horizontal ? "horizontal" : "vertical" });
            Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
            JsonElement changed = result.GetProperty("during");
            JsonElement[] originals = before.GetProperty("objects").EnumerateArray().ToArray(), actual = changed.GetProperty("objects").EnumerateArray().ToArray();
            Assert.Contains(originals, o => o.GetProperty("kind").GetString() == "Function");
            Assert.Contains(originals, o => o.GetProperty("kind").GetString() == "Text");
            Assert.Contains(originals, o => o.GetProperty("kind").GetString() == "Line");
            Assert.Contains(originals, o => o.GetProperty("kind").GetString() == "Rectangle");
            Assert.Contains(originals, o => o.GetProperty("kind").GetString() == "SymbolReference");
            Assert.Contains(originals, o => o.GetProperty("kind").GetString() == "PotentialDefinition");
            foreach (JsonElement original in originals)
            {
                if (original.GetProperty("angle").ValueKind == JsonValueKind.Number)
                { Assert.Equal(0, original.GetProperty("angle").GetDouble()); }
                Assert.All(original.GetProperty("children").EnumerateArray(), child => Assert.Equal(0, child.GetProperty("angle").GetDouble()));
            }
            if (operation == "delete")
            {
                Assert.NotEmpty(before.GetProperty("connections").EnumerateArray());
                Assert.Empty(actual);
                Assert.Empty(changed.GetProperty("connections").EnumerateArray());
                Assert.DoesNotContain(changed.GetProperty("functions").EnumerateArray(), f => f.GetProperty("page").GetString() == page);
                Assert.Equal(before.GetProperty("functions").GetArrayLength() - originals.Count(o => o.GetProperty("kind").GetString() == "Function"), changed.GetProperty("functions").GetArrayLength());
                Assert.DoesNotContain(changed.GetProperty("functions").EnumerateArray(), f => ids.Contains(f.GetProperty("oid").GetInt64()));
            }
            else
            {
                if (operation == "copy")
                { Assert.Equal(before.GetProperty("functions").GetArrayLength() + originals.Count(o => o.GetProperty("kind").GetString() == "Function"), changed.GetProperty("functions").GetArrayLength()); }
                Assert.Equal(operation == "copy" ? originals.Length * 2 : originals.Length, actual.Length);
                foreach (JsonElement original in originals)
                {
                    JsonElement found = operation == "copy"
                        ? Assert.Single(actual, a => !ids.Contains(a.GetProperty("oid").GetInt64()) && a.GetProperty("kind").GetString() == original.GetProperty("kind").GetString() && a.GetProperty("name").GetRawText() == original.GetProperty("name").GetRawText())
                        : Assert.Single(actual, a => a.GetProperty("oid").GetInt64() == original.GetProperty("oid").GetInt64());
                    Assert.Equal(original.GetProperty("name").GetRawText(), found.GetProperty("name").GetRawText());
                    Assert.Equal(original.GetProperty("text").GetRawText(), found.GetProperty("text").GetRawText());
                    string kind = original.GetProperty("kind").GetString()!;
                    double x = original.GetProperty("location")[0].GetDouble(), y = original.GetProperty("location")[1].GetDouble();
                    double[] Transform(double px, double py) => operation == "rotate" ? [x - py + y, y + px - x] : operation == "copy" ? [px, py - 100] : horizontal ? [px, 300 - py] : [420 - px, py];
                    double[] box = original.GetProperty("bounds").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    double[][] corners = [Transform(box[0], box[1]), Transform(box[0], box[3]), Transform(box[2], box[1]), Transform(box[2], box[3])];
                    double[] expectedBox = [corners.Min(p => p[0]), corners.Min(p => p[1]), corners.Max(p => p[0]), corners.Max(p => p[1])];
                    for (int i = 0; i < 4; i++)
                    { Assert.Equal(expectedBox[i], found.GetProperty("bounds")[i].GetDouble(), 5); }
                    if (original.GetProperty("points").ValueKind == JsonValueKind.Array)
                    { for (int i = 0; i < 2; i++) { double[] at = Transform(original.GetProperty("points")[i][0].GetDouble(), original.GetProperty("points")[i][1].GetDouble()); Assert.Equal(at[0], found.GetProperty("points")[i][0].GetDouble(), 6); Assert.Equal(at[1], found.GetProperty("points")[i][1].GetDouble(), 6); } }
                    Assert.Equal(kind == "Rectangle" ? expectedBox[0] : operation == "mirror" && !horizontal ? 420 - x : x, found.GetProperty("location")[0].GetDouble(), 6);
                    Assert.Equal(kind == "Rectangle" ? expectedBox[1] : operation == "copy" ? y - 100 : operation == "mirror" && horizontal ? 300 - y : y, found.GetProperty("location")[1].GetDouble(), 6);
                    if (operation == "rotate" && original.GetProperty("angle").ValueKind == JsonValueKind.Number)
                    { Assert.Equal(kind == "Rectangle" ? 0 : Math.PI / 2, found.GetProperty("angle").GetDouble(), 6); }
                    // Independent observation includes relative property offsets, text contents, angles and every pin.
                    Assert.Equal(original.GetProperty("children").GetArrayLength(), found.GetProperty("children").GetArrayLength());
                    int childCount = original.GetProperty("children").GetArrayLength();
                    Assert.True(kind == "Function" ? childCount is 12 or 13 : kind == "PotentialDefinition" ? childCount == 2 : childCount == 0);
                    if (operation == "mirror" && original.GetProperty("angle").ValueKind == JsonValueKind.Number)
                    { Assert.Equal(0, found.GetProperty("angle").GetDouble(), 6); }
                    for (int i = 0; i < original.GetProperty("pins").GetArrayLength(); i++)
                    {
                        JsonElement pin = original.GetProperty("pins")[i], next = found.GetProperty("pins")[i];
                        double px = pin.GetProperty("location")[0].GetDouble(), py = pin.GetProperty("location")[1].GetDouble();
                        string direction = pin.GetProperty("direction").GetString()!;
                        string expectedDirection = operation == "rotate" ? direction switch { "Right" => "Up", "Up" => "Left", "Left" => "Down", "Down" => "Right", _ => throw new InvalidOperationException() }
                            : operation == "copy" ? direction : horizontal ? direction switch { "Up" => "Down", "Down" => "Up", _ => direction } : direction switch { "Left" => "Right", "Right" => "Left", _ => direction };
                        Assert.Equal(expectedDirection, next.GetProperty("direction").GetString());
                        double[] expectedPin = Transform(px, py);
                        // B12: the seeded PCP's variants have unequal offsets; K's mirrored upper pin is +3.99.
                        if (kind == "PotentialDefinition")
                        {
                            Assert.Equal([x, y + 3], pin.GetProperty("location").EnumerateArray().Select(v => v.GetDouble()));
                            if (operation == "rotate")
                            { expectedPin = [x - 2, y]; }
                            else if (operation == "mirror" && horizontal)
                            { expectedPin = [x, 300 - y - 4]; }
                        }
                        else if (kind == "Function" && operation == "mirror" && horizontal && i == 1)
                        {
                            Assert.Equal([x, y - 4], pin.GetProperty("location").EnumerateArray().Select(v => v.GetDouble()));
                            expectedPin = [x, 300 - y + 3.99];
                        }
                        Assert.Equal(expectedPin[0], next.GetProperty("location")[0].GetDouble(), 6);
                        Assert.Equal(expectedPin[1], next.GetProperty("location")[1].GetDouble(), 6);
                    }
                    for (int i = 0; i < original.GetProperty("children").GetArrayLength(); i++)
                    {
                        JsonElement child = original.GetProperty("children")[i], next = found.GetProperty("children")[i];
                        double cx = child.GetProperty("location")[0].GetDouble(), cy = child.GetProperty("location")[1].GetDouble();
                        Assert.Equal(child.GetProperty("text").GetRawText(), next.GetProperty("text").GetRawText());
                        // B12: only the observed anchor slots change offset; the other entries retain local offsets.
                        bool anchor = kind == "PotentialDefinition" ? i == 0 : i == 0 || i == childCount - 4 || i == childCount - 2 || childCount == 13 && i == 8;
                        Assert.Equal(anchor && operation == "rotate" ? -cy : anchor && operation == "mirror" && !horizontal ? -cx : cx, next.GetProperty("location")[0].GetDouble(), 6);
                        Assert.Equal(anchor && operation == "rotate" ? cx : anchor && operation == "mirror" && horizontal ? -cy : cy, next.GetProperty("location")[1].GetDouble(), 6);
                        double angle = next.GetProperty("angle").GetDouble();
                        Assert.Equal(0, (angle + 2 * Math.PI) % (2 * Math.PI), 6);
                    }
                }
            }
            if (rollback)
            {
                Assert.Equal(before.GetRawText(), result.GetProperty("after").GetRawText());
                await session.UndoAsync();
                Assert.DoesNotContain((await session.SnapshotAsync()).Pages, p => p.Name == sentinel);
            }
            else
            {
                await session.UndoAsync();
                JsonElement undone = await Probe(new { project = session.Project, page, operation = "read" });
                Assert.Equal(before.GetRawText(), undone.GetProperty("after").GetRawText());
                Assert.Contains((await session.SnapshotAsync()).Pages, p => p.Name == sentinel);
                await session.UndoAsync();
                Assert.DoesNotContain((await session.SnapshotAsync()).Pages, p => p.Name == sentinel);
            }
            if (operation == "rotate" && !rollback)
            {
                foreach (JsonElement target in originals)
                {
                    double x = target.GetProperty("location")[0].GetDouble(), y = target.GetProperty("location")[1].GetDouble();
                    JsonElement pivoted = await Probe(new { project = session.Project, page, operation = "rotate", oids = new[] { target.GetProperty("oid").GetInt64() }, pivot = new[] { x + 4, y + 4 } });
                    JsonElement moved = pivoted.GetProperty("after").GetProperty("objects").EnumerateArray().Single(o => o.GetProperty("oid").GetInt64() == target.GetProperty("oid").GetInt64());
                    double height = target.GetProperty("bounds")[3].GetDouble() - target.GetProperty("bounds")[1].GetDouble();
                    Assert.Equal(target.GetProperty("kind").GetString() == "Rectangle" ? x + 8 - height : x + 8, moved.GetProperty("location")[0].GetDouble(), 6);
                    Assert.Equal(y, moved.GetProperty("location")[1].GetDouble(), 6);
                    await session.UndoAsync();
                }
            }
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }
    [Theory]
    [InlineData("copy")]
    [InlineData("rotate")]
    [InlineData("mirror")]
    [InlineData("align")]
    [InlineData("distribute")]
    [InlineData("delete")]
    public async Task Product_UsesFindLocatorsDryRunAndOneUndo(string operation)
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await host.CallAsync("eplan_placement_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["items"] = ((int[])[40, 100, 180]).Select((x, i) => new { kind = "rectangle", corners = new[] { new[] { x, 200 - i * 20 }, new[] { x + 10, 210 - i * 20 } } }).ToArray()
        });
        JsonElement found = await host.CallAsync("eplan_object_find", new() { ["project"] = session.Project, ["page"] = page, ["object_type"] = "Rectangle" });
        Assert.Equal("supported", found.GetProperty("status").GetString());
        Assert.False(found.GetProperty("truncated").GetBoolean());
        JsonElement[] locators = found.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("locator")).ToArray();
        Assert.Equal(3, locators.Length);
        var args = new Dictionary<string, object?> { ["project"] = session.Project, ["page"] = page, ["operation"] = operation, ["locators"] = operation is "align" or "distribute" ? locators : locators.Take(1).ToArray(), ["response_format"] = "detailed" };
        switch (operation)
        {
            case "copy":
                args["target_mm"] = new[] { 80, 100 };
                args["copies"] = 2;
                args["step_mm"] = new[] { 40, 0 };
                break;
            case "rotate":
                args["quarter_turns"] = 1;
                break;
            case "mirror":
                args["axis"] = "horizontal";
                args["coordinate_mm"] = 150;
                break;
            case "align":
                args["alignment"] = "left";
                break;
            case "distribute":
                args["axis"] = "horizontal";
                break;
        }
        await using var bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        async Task<string> Geometry() => JsonSerializer.Serialize(await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken));
        string baseline = await Geometry(), sentinel = Assert.Single(await session.CreatePagesAsync(1));
        JsonElement preview = await host.CallAsync("eplan_placement_edit", new(args) { ["dry_run"] = true });
        Assert.Equal(ExecutionState.NotStarted, preview.GetProperty("execution").GetString());
        Assert.Equal(baseline, await Geometry());
        if (operation == "delete")
        {
            Assert.Empty(preview.GetProperty("removedConnections").EnumerateArray());
            JsonElement removed = Assert.Single(preview.GetProperty("predicted").EnumerateArray());
            Assert.Equal("Rectangle", removed.GetProperty("kind").GetString());
            Assert.False(removed.GetProperty("exists").GetBoolean());
            JsonElement locator = removed.GetProperty("locator");
            string position = JsonSerializer.Serialize(removed.GetProperty("location"));
            string angle = removed.GetProperty("angle").GetDouble().ToString(CultureInfo.InvariantCulture);
            host.Approvals.Approve("eplan_placement_edit", session.Project,
                [$"update placement {page}:{locator.GetProperty("type")}/{locator.GetProperty("oid")}: delete '' -> 'position={position}; angle(rad)={angle}; tag='"]);
        }
        JsonElement result = await host.CallAsync("eplan_placement_edit", args);
        TestContext.Current.TestOutputHelper!.WriteLine(operation + " product: " + result);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        if (operation == "copy")
        { Assert.Equal(2, result.GetProperty("newLocators").GetArrayLength()); }
        if (operation == "delete")
        { Assert.False(string.IsNullOrEmpty(result.GetProperty("backup").GetString())); }
        await session.UndoAsync();
        Assert.Equal(baseline, await Geometry());
        Assert.Contains((await session.SnapshotAsync()).Pages, p => p.Name == sentinel);
    }
    private async Task Seed(string page)
    {
        await host.CallAsync("eplan_device_place", new() { ["project"] = host.Writable, ["page"] = page, ["library"] = "IEC_symbol", ["symbol"] = "K", ["symbol_variant"] = 0, ["x"] = 40, ["y"] = 200, ["tag"] = "-EDIT" + Guid.NewGuid().ToString("N")[..6], ["display_properties"] = new[] { new { property_id = 20214, offset_x_mm = 8, offset_y_mm = 8 } } });
        await host.CallAsync("eplan_device_place", new() { ["project"] = host.Writable, ["page"] = page, ["library"] = "IEC_symbol", ["symbol"] = "K", ["symbol_variant"] = 0, ["x"] = 40, ["y"] = 120, ["tag"] = "-EDIT" + Guid.NewGuid().ToString("N")[..6] });
        await host.CallAsync("eplan_placement_create", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["items"] = new object[] {
            new { kind = "text", position = (int[])[100, 200], height_mm = 3.5, contents = new Dictionary<string, string> { ["??_??"] = "Mirror note" } },
            new { kind = "line", points = (int[][])[[160, 200], [170, 210]] }, new { kind = "rectangle", corners = (int[][])[[220, 200], [230, 210]] } }
        });
        await host.CallAsync("eplan_connection_generate", new() { ["project"] = host.Writable, ["pages"] = new[] { page } });
    }
    private async Task<JsonElement> Probe(object request)
    {
        string file = Path.Combine(host.Home, "placement-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargePlacementSpike /REQUEST:\"" + file + "\"");
        string observed = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine(observed);
        return JsonDocument.Parse(observed).RootElement.Clone();
    }
}

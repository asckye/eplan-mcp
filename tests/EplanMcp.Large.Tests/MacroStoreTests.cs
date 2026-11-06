using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class MacroStoreTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task WindowMacro_StoresTwoGraphics_InsertsOnAnotherNewPage_AndOneUndoRestoresIt()
    {
        string macroRoot = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_MACRO_ROOT") ?? "";
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await session.CreatePagesAsync(2);
        await CreateGraphics(pages[0]);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        string before = (await session.SnapshotAsync()).Hash;
        string sourceBefore = await DrawingHash(bridge, pages[0]), targetBefore = await DrawingHash(bridge, pages[1]);
        var locators = new List<JsonElement>();
        foreach (string kind in new[] { "Line", "Rectangle" })
        {
            JsonElement found = await host.CallAsync("eplan_object_find", new() { ["project"] = host.Writable, ["page"] = pages[0], ["object_type"] = kind });
            locators.Add(Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("locator").Clone());
        }
        string name = "large_store_" + Guid.NewGuid().ToString("N");
        var args = new Dictionary<string, object?> { ["project"] = host.Writable, ["operation"] = "window", ["page"] = pages[0], ["locators"] = locators, ["name"] = name };
        JsonElement preview = await host.CallAsync("eplan_macro_store", args);
        Assert.Equal(ExecutionState.NotStarted, preview.GetProperty("execution").GetString());
        Assert.False(File.Exists(preview.GetProperty("path").GetString()));
        args["dry_run"] = false;
        JsonElement stored = await host.CallAsync("eplan_macro_store", args);
        VerifyFile(stored, ".ema");
        Assert.Equal([0], stored.GetProperty("variants").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        Assert.Equal(sourceBefore, await DrawingHash(bridge, pages[0]));
        Assert.Equal(targetBefore, await DrawingHash(bridge, pages[1]));
        // Inserting needs a copy in EPLAN's macro directory, outside scratch: only with the variable an engineer sets on purpose.
        if (!Path.IsPathFullyQualified(macroRoot) || !Directory.Exists(macroRoot))
        { return; }
        string macro = name + ".ema", copied = Path.Combine(macroRoot, macro);
        File.Copy(stored.GetProperty("path").GetString()!, copied, overwrite: false);
        try
        {
            JsonElement inserted = await host.CallAsync("eplan_macro_insert", new()
            {
                ["project"] = host.Writable,
                ["page"] = pages[1],
                ["macro"] = macro,
                ["x"] = 40,
                ["y"] = 120,
                ["macro_box"] = false,
                ["layout_guard"] = "warn",
                ["dry_run"] = false,
            });
            Assert.Equal(2, inserted.GetProperty("objectCount").GetInt32());
            PageDrawingGeometry drawing = await ReadDrawing(bridge, pages[1]);
            Assert.Single(drawing.Placements, p => p.Kind == "Line");
            Assert.Single(drawing.Placements, p => p.Kind == "Rectangle");
            Assert.NotEqual(targetBefore, await DrawingHash(bridge, pages[1]));
            await session.UndoAsync();
            Assert.Equal(targetBefore, await DrawingHash(bridge, pages[1]));
            Assert.Equal(sourceBefore, await DrawingHash(bridge, pages[0]));
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        }
        finally { File.Delete(copied); }
    }

    [Fact]
    public async Task PageMacro_StoresTheNewPageAndLeavesTheProjectUnchanged()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await CreateGraphics(page);
        string before = (await session.SnapshotAsync()).Hash;
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        string drawingBefore = await DrawingHash(bridge, page);
        JsonElement stored = await host.CallAsync("eplan_macro_store", new()
        {
            ["project"] = host.Writable,
            ["operation"] = "page",
            ["pages"] = new[] { page },
            ["name"] = "large_pages_" + Guid.NewGuid().ToString("N"),
            ["description"] = new Dictionary<string, string> { ["en_US"] = "Macro store Large test", ["zh_CN"] = "页宏存储测试" },
            ["dry_run"] = false,
        });
        VerifyFile(stored, ".emp");
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        Assert.Equal(drawingBefore, await DrawingHash(bridge, page));
    }

    private Task<JsonElement> CreateGraphics(string page) => host.CallAsync("eplan_placement_create", new()
    {
        ["project"] = host.Writable,
        ["page"] = page,
        ["dry_run"] = false,
        ["items"] = new object[] { new { kind = "line", points = (double[][])[[40, 40], [80, 40]] }, new { kind = "rectangle", corners = (double[][])[[100, 40], [130, 60]] } },
    });
    private static void VerifyFile(JsonElement result, string extension)
    {
        string path = result.GetProperty("path").GetString()!;
        Assert.EndsWith(extension, path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new FileInfo(path).Length, result.GetProperty("size").GetInt64());
        Assert.True(new FileInfo(path).Length > 0);
        using FileStream file = File.OpenRead(path);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file)), result.GetProperty("sha256").GetString());
    }
    private Task<PageDrawingGeometry> ReadDrawing(BridgeClient bridge, string page) => bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
        new() { Project = host.Writable, Page = page }, TestContext.Current.CancellationToken);
    private async Task<string> DrawingHash(BridgeClient bridge, string page) => SnapshotCanonical.Sha256(JsonSerializer.Serialize(await ReadDrawing(bridge, page)));
}

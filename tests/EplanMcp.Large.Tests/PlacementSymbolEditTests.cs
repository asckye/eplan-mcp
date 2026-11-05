using System.Text.Json;
using EplanMcp.Contracts;
namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementSymbolEditTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly int[][] _linePoints = [[100, 100], [120, 100]];
    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 6)]
    public async Task ExistingBpOrMainFunction_VariantReadback_OneUndo(bool main, int variant)
    {
        Assert.NotNull(host);
        await using var session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await Call(main ? "eplan_device_place" : "eplan_connection_create", main
            ? new() { ["project"] = session.Project, ["page"] = page, ["part"] = "SIE.3VA1032-2ED32-0AA0", ["library"] = "IEC_symbol", ["symbol"] = "Q_3LI", ["symbol_variant"] = 0, ["x"] = 100, ["y"] = 100, ["tag"] = "-QROUND10", ["dry_run"] = false }
            : await BpRequest(session.Project, page));
        JsonElement locator = await Find(session.Project, page, main ? "Function" : "InterruptionPoint");
        string baseline = (await session.SnapshotAsync(page)).Hash;
        JsonElement? deviceBefore = main ? await Call("eplan_device_get", new() { ["project"] = session.Project, ["tag"] = "-QROUND10" }) : null;
        var args = new Dictionary<string, object?> { ["project"] = session.Project, ["page"] = page, ["operation"] = "set_variant", ["symbol_variant"] = variant, ["locators"] = new[] { locator }, ["dry_run"] = false };
        try
        {
            JsonElement result = await Call("eplan_placement_edit", args);
            Assert.Equal(variant, result.GetProperty("predicted")[0].GetProperty("symbol").GetProperty("variant").GetInt32());
            JsonElement fresh = await Find(session.Project, page, main ? "Function" : "InterruptionPoint");
            await ReadProperties(session.Project, fresh, main ? "function" : "interruptionpoint");
            if (main)
            {
                JsonElement after = await Call("eplan_device_get", new() { ["project"] = session.Project, ["tag"] = "-QROUND10" });
                Assert.Equal(deviceBefore!.Value.GetProperty("parts").GetRawText(), after.GetProperty("parts").GetRawText());
                Assert.Equal(deviceBefore.Value.GetProperty("device").GetProperty("pins").GetRawText(), after.GetProperty("device").GetProperty("pins").GetRawText());
            }
            await session.UndoAsync();
            Assert.Equal(baseline, (await session.SnapshotAsync(page)).Hash);
        }
        catch { await Find(session.Project, page, main ? "Function" : "InterruptionPoint"); throw; }
    }
    [Fact]
    public async Task LineHasNoSymbol_RefusedWithoutPageChange()
    {
        Assert.NotNull(host);
        await using var session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await Call("eplan_placement_create", new() { ["project"] = session.Project, ["page"] = page, ["items"] = new[] { new { kind = "line", points = _linePoints } }, ["dry_run"] = false });
        string baseline = (await session.SnapshotAsync(page)).Hash;
        var request = new Dictionary<string, object?> { ["project"] = session.Project, ["page"] = page, ["operation"] = "set_variant", ["symbol_variant"] = 4, ["locators"] = new[] { await Find(session.Project, page, "Line") }, ["dry_run"] = false };
        TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
        (bool ok, JsonElement result) = await eplan.CallOrErrorAsync("eplan_placement_edit", request);
        TestContext.Current.TestOutputHelper.WriteLine("result: " + result.GetRawText());
        await Find(session.Project, page, "Line");
        Assert.False(ok);
        Assert.Equal(baseline, (await session.SnapshotAsync(page)).Hash);
    }
    private async Task<Dictionary<string, object?>> BpRequest(string project, string page)
    {
        JsonElement symbols = await Call("eplan_symbol_search", new() { ["project"] = project, ["library"] = "SPECIAL", ["name_contains"] = "BP", ["limit"] = 200 });
        JsonElement bp = Assert.Single(symbols.GetProperty("symbols").EnumerateArray(), s => s.GetProperty("name").GetString() == "BP" && s.GetProperty("variant").GetInt32() == 0);
        string direction = bp.GetProperty("connectionPoints")[0].GetProperty("direction").GetString()!;
        return new() { ["project"] = project, ["page"] = page, ["items"] = new[] { new { kind = "interruption_point", position = new[] { 100, 100 }, name = "=GAB1-24V", symbol = "BP", symbol_variant = 0, direction } }, ["dry_run"] = false };
    }
    private async Task<JsonElement> Find(string project, string page, string kind)
    {
        JsonElement found = await Call("eplan_object_find", new() { ["project"] = project, ["page"] = page, ["object_type"] = kind });
        return Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("locator").Clone();
    }
    private Task<JsonElement> ReadProperties(string project, JsonElement locator, string kind) => Call("eplan_property_get", new()
    {
        ["project"] = project,
        ["queries"] = new[] { new { target = new { locator = new { project, kind, type = locator.GetProperty("type").GetInt32(), oid = locator.GetProperty("oid").GetInt64() } }, properties = new[] { new { property_id = 19307 } } } }
    });
    private async Task<JsonElement> Call(string tool, Dictionary<string, object?> args)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(tool + " request: " + JsonSerializer.Serialize(args));
        (bool success, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
        TestContext.Current.TestOutputHelper.WriteLine("result/readback: " + result.GetRawText());
        if (!success && args.TryGetValue("project", out object? project) && args.TryGetValue("page", out object? page))
        {
            try
            {
                await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
                PageDrawingGeometry drawing = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, new() { Project = (string)project!, Page = (string)page! }, TestContext.Current.CancellationToken);
                TestContext.Current.TestOutputHelper.WriteLine("failure readback: " + JsonSerializer.Serialize(drawing));
            }
            catch (Exception error) { TestContext.Current.TestOutputHelper.WriteLine("failure readback error: " + error); }
        }
        Assert.True(success, result.GetRawText());
        return result;
    }
}

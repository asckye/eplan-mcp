using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class BlockCreateTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task ThreeLinesFormNamedBlockAndOneUndoRestoresThem()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        var create = new { project = host.Writable, page, dry_run = false, snap_to_grid = false, items = Enumerable.Range(0, 3).Select(i => new { kind = "line", points = new[] { new[] { 40d, 40d + i * 20 }, new[] { 60d, 40d + i * 20 } } }).ToArray() };
        await Call("eplan_placement_create", create);
        var read = new { project = host.Writable, page, include_drawings = true, drawing_limit = 100, response_format = "detailed" };
        JsonElement before = await Call("eplan_page_get", read);
        JsonElement[] lines = before.GetProperty("drawings").GetProperty("items").EnumerateArray().Where(r => r.GetProperty("kind").GetString() == "Line").ToArray();
        Assert.Equal(3, lines.Length);
        string hash = await Hash();
        var request = new { project = host.Writable, page, dry_run = false, items = new[] { new { kind = "block", name = "144_268557", members = lines.Select(r => r.GetProperty("locator")).ToArray() } } };
        try
        {
            JsonElement result = await Call("eplan_placement_create", request);
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            JsonElement after = await Call("eplan_page_get", read);
            JsonElement block = Assert.Single(after.GetProperty("drawings").GetProperty("items").EnumerateArray(), r => r.GetProperty("kind").GetString() == "Block");
            Assert.Equal("144_268557", block.GetProperty("properties").GetProperty("name").GetString());
            Assert.Equal("3", block.GetProperty("properties").GetProperty("count").GetString());
            await session.UndoAsync();
            Assert.Equal(hash, await Hash());
        }
        catch
        { Evidence("request", request); Evidence("readback", await Call("eplan_page_get", read)); throw; }
        async Task<string> Hash() => SnapshotCanonical.Sha256(JsonSerializer.Serialize(await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, new() { Project = host.Writable, Page = page }, TestContext.Current.CancellationToken)));
    }
    private async Task<JsonElement> Call(string tool, object request)
    {
        Evidence("request", new { tool, request });
        try
        { JsonElement result = await host.CallAsync(tool, JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(request))!); Evidence("result", result); return result; }
        catch (Exception ex) { Evidence("failed result", ex.ToString()); throw; }
    }
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}

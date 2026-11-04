using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class DeferredConnectionTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task DeferredNodes_GenerateOnce_AndEachCallHasOneUndo()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        async Task<PageDrawingGeometry> Drawing() => await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
        async Task<JsonElement> Call(string tool, Dictionary<string, object?> request)
        {
            TestContext.Current.TestOutputHelper!.WriteLine(tool + " request: " + JsonSerializer.Serialize(request));
            (bool ok, JsonElement result) = await eplan.CallOrErrorAsync(tool, request);
            TestContext.Current.TestOutputHelper.WriteLine(tool + " result: " + result.GetRawText());
            TestContext.Current.TestOutputHelper.WriteLine("drawing readback: " + JsonSerializer.Serialize(await Drawing()));
            Assert.True(ok, result.GetRawText());
            return result;
        }
        async Task<JsonElement[]> Connections() => (await Call("eplan_connection_list", new()
        { ["project"] = session.Project, ["page"] = page, ["response_format"] = "detailed" })).GetProperty("connections").EnumerateArray().ToArray();
        string suffix = page[(page.LastIndexOf('/') + 1)..];
        for (int i = 0; i < 2; i++)
        {
            await Call("eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["dry_run"] = false,
                ["terminal_strip"] = "=GAA+A1-XR4D" + suffix,
                ["terminal_number"] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["symbol_variant"] = 0,
                ["x"] = i == 0 ? 80 : 160,
                ["y"] = i == 0 ? 200 : 80
            });
        }
        Assert.Empty(await Connections());
        string before = (await session.SnapshotAsync(page)).Hash;
        PageDrawingGeometry beforeDrawing = await Drawing();
        var request = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["generate"] = false,
            ["response_format"] = "detailed",
            ["items"] = new object[]
            {
                new { kind = "t_node", position = new[] { 80, 120 }, directions = new[] { "Up", "Down", "Right" } },
                new { kind = "corner", position = new[] { 160, 120 }, directions = new[] { "Left", "Down" } },
                new { kind = "interruption_point", position = new[] { 80, 60 }, direction = "Up", name = "=GAA+A1-R4D" + suffix }
            }
        };
        JsonElement placed = await Call("eplan_connection_create", request);
        Assert.Equal(1, placed.GetProperty("undoSteps").GetInt32());
        Assert.Equal("placement_only", placed.GetProperty("coverage").GetString());
        Assert.Empty(await Connections());
        Assert.True((await Drawing()).Placements.Count > beforeDrawing.Placements.Count);
        await Call("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = new[] { page }, ["dry_run"] = false });
        Assert.NotEmpty(await Connections());
        await session.UndoAsync();
        Assert.Empty(await Connections());
        Assert.True((await Drawing()).Placements.Count > beforeDrawing.Placements.Count);
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
        Assert.Equal(JsonSerializer.Serialize(beforeDrawing), JsonSerializer.Serialize(await Drawing()));
    }
}

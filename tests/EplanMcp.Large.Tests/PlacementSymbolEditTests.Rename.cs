using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class PlacementSymbolEditTests
{
    [Fact]
    public async Task InterruptionPoint_IsRenamedToATerminalStyleName_ReadBack_UndoRestores()
    {
        await using var session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await Call("eplan_connection_create", await BpRequest(session.Project, page));
        JsonElement locator = await Find(session.Project, page, "InterruptionPoint");
        string oldName = await PageNameAsync();
        var args = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["locators"] = new[] { locator },
            ["operation"] = "rename",
            ["name"] = "-XD4:2c"
        };
        JsonElement preview = await Call("eplan_placement_edit", args);
        string expected = Assert.Single(preview.GetProperty("predicted").EnumerateArray()).GetProperty("name").GetString()!;
        Assert.Equal("=" + DrawingNames.HigherLevelFunctionOf(page) + "-XD4:2c", expected);
        args["dry_run"] = false;
        JsonElement result = await Call("eplan_placement_edit", args);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        JsonElement row = Assert.Single(result.GetProperty("predicted").EnumerateArray());
        Assert.Equal(expected, row.GetProperty("name").GetString());
        TestContext.Current.TestOutputHelper!.WriteLine("visibleName: " + row.GetProperty("visibleName"));
        Assert.Equal(expected, await PageNameAsync());
        await session.UndoAsync();
        Assert.Equal(oldName, await PageNameAsync());

        async Task<string> PageNameAsync()
        {
            await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            PageDrawingGeometry drawing = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
                new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
            DrawingPlacement point = Assert.Single(drawing.Placements, p => p.Parent is null && p.Kind == "InterruptionPoint");
            TestContext.Current.TestOutputHelper!.WriteLine("page interruption: " + JsonSerializer.Serialize(point));
            return point.Properties["name"];
        }
    }
}

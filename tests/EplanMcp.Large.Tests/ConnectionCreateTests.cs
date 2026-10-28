using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Tests;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class ConnectionCreateTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData("corner", "Up,Left")]
    [InlineData("corner", "Up,Right")]
    [InlineData("corner", "Down,Left")]
    [InlineData("corner", "Down,Right")]
    [InlineData("t_node", "Up,Left,Right")]
    [InlineData("t_node", "Down,Left,Right")]
    [InlineData("t_node", "Up,Down,Left")]
    [InlineData("t_node", "Up,Down,Right")]
    [InlineData("potential_point", "Up")]
    [InlineData("potential_point", "Down")]
    [InlineData("potential_point", "Left")]
    [InlineData("potential_point", "Right")]
    public async Task SupportedDirections_ActualLogicalPinsMatchPrediction_TwoUndosRestoreSnapshot(string kind, string directions)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = await NewPageAsync(session);
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running T3b Large tests.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSetup /PAGE:\"" + page + "\" /DIRECTIONS:" + directions);
            JsonElement drawing = await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true });
            // NewPageAsync's numbered Circuit page, then native S40 setup: no dependency on T2b's symbol branch.
            Assert.True(drawing.GetProperty("deviceCount").GetInt32() >= directions.Split(',').Length);
            await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
                new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
            string before = (await session.SnapshotAsync()).Hash;
            var layout = new ConnectionCreateLayout(geometry.GridSize, directions.Split(','));
            var items = new List<object> { new { kind, position = layout.Position, directions = directions.Split(',') } };
            if (layout.Corner is { } corner)
            { items.Add(new { kind = "corner", position = corner.Position, directions = corner.Directions }); }
            var args = new Dictionary<string, object?>
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["items"] = JsonSerializer.SerializeToElement(items)
            };
            JsonElement preview = await eplan.CallAsync("eplan_connection_create", new(args) { ["dry_run"] = true, ["response_format"] = "detailed" });
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            JsonElement result = await eplan.CallAsync("eplan_connection_create", new(args) { ["response_format"] = "detailed" });
            Assert.Equal((ExecutionState.Committed, 2), (result.GetProperty("execution").GetString(), result.GetProperty("undoSteps").GetInt32()));
            string[] expected = preview.GetProperty("logicalAfterGenerate").EnumerateArray().SelectMany(g => g.GetProperty("pins").EnumerateArray().Select(p => p.GetString()!)).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(layout.Targets.Count, expected.Length);
            Assert.Equal(layout.Targets.Count + (layout.Corner is null ? 0 : 1), preview.GetProperty("graphicalAutoconnect").GetArrayLength());
            JsonElement[] pairs = result.GetProperty("actualEndpoints").EnumerateArray().ToArray();
            Assert.NotEmpty(pairs);
            Assert.Equal(expected, pairs.SelectMany(p => p.EnumerateArray()).Where(p => p.ValueKind != JsonValueKind.Null).Select(p => p.GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            if (kind == "potential_point")
            { Assert.All(pairs, pair => Assert.Equal(JsonValueKind.Null, pair[0].ValueKind)); }
            JsonElement connections = await eplan.CallAsync("eplan_connection_list", new() { ["project"] = session.Project, ["page"] = page });
            Assert.NotEmpty(connections.GetProperty("connections").EnumerateArray());
            JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = session.Project, ["pages"] = JsonSerializer.SerializeToElement(new[] { page }) });
            TestContext.Current.TestOutputHelper!.WriteLine($"{kind} {directions}: actual StartPin/EndPin={result.GetProperty("actualEndpoints")}; image={image.GetProperty("directory")}");
            // The business snapshot holds functions and connections, not bare connection symbols, so count those
            // on the page: the first undo removes only the generated connections, the second the placed symbols.
            int placed = await SymbolsAsync(session.Project, page);
            await session.UndoAsync();
            Assert.Empty((await eplan.CallAsync("eplan_connection_list", new() { ["project"] = session.Project, ["page"] = page })).GetProperty("connections").EnumerateArray());
            Assert.Equal(placed, await SymbolsAsync(session.Project, page));
            await session.UndoAsync();
            Assert.Equal(placed - items.Count, await SymbolsAsync(session.Project, page));
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            // S40 has no reproducible native generation failure; fake Bridge/core tests inject it before commit.
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }

    private async Task<int> SymbolsAsync(string project, string page) =>
        (await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200 }))
            .GetProperty("drawings").GetProperty("items").EnumerateArray().Count(p => p.GetProperty("kind").GetString() is not ("Function" or "PropertyPlacement")); // fixture devices are Functions

    private static async Task<string> NewPageAsync(PlanStepLiveSession session) => Assert.Single(await session.CreatePagesAsync(1));
}

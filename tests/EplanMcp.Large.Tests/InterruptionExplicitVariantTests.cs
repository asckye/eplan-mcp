using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class InterruptionExplicitVariantTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task RightBpZero_ReadsExactVariant_OneUndoRestores()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await session.CreatePagesAsync(2);
        string page = pages[0];
        await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        string before = (await session.SnapshotAsync(page)).Hash;
        var request = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["generate"] = false,
            ["items"] = new[] { new { kind = "interruption_point", name = "=GAA-2L1", symbol = "BP", symbol_variant = 0, direction = "Right", position = new[] { 100, 100 }, counterpart_pages = new[] { pages[1] } } },
        };
        TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
        (bool ok, JsonElement result) = await eplan.CallOrErrorAsync("eplan_connection_create", request);
        TestContext.Current.TestOutputHelper.WriteLine("result: " + result.GetRawText());
        PageDrawingGeometry actual = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper.WriteLine("readback: " + JsonSerializer.Serialize(actual));
        Assert.True(ok, result.GetRawText());
        JsonElement symbol = Assert.Single(result.GetProperty("symbols").EnumerateArray()).GetProperty("symbol");
        Assert.Equal(0, symbol.GetProperty("variant").GetInt32());
        JsonElement readback = Assert.Single(result.GetProperty("interruptionPoints").EnumerateArray());
        Assert.Equal(0, readback.GetProperty("symbol").GetProperty("variant").GetInt32());
        Assert.Contains(readback.GetProperty("pins").EnumerateArray(), p => p.GetProperty("direction").GetString() == "Right");
        DrawingPlacement point = Assert.Single(actual.Placements, p => p.Kind == "InterruptionPoint");
        Assert.Equal("=GAA-2L1", point.Properties["name"]);
        // The successful result includes the adapter's committed SymbolVariant readback.
        Assert.Equal(1, result.GetProperty("undoSteps").GetInt32());
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
    }
}

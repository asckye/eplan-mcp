using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementArrangementTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectArrangementWhilePlacing_ReadsBack_OneUndoRestores(bool interruption)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = (await session.CreatePagesAsync(1))[0];
        string before = (await session.SnapshotAsync(page)).Hash;
        var request = new Dictionary<string, object?>
        { ["project"] = session.Project, ["page"] = page, ["dry_run"] = false, ["layout_guard"] = "warn" };
        string tool;
        if (interruption)
        {
            tool = "eplan_connection_create";
            request["generate"] = false;
            request["items"] = new[] { new { kind = "interruption_point", name = "=GAB1-24V", symbol = "BP", symbol_variant = 0, direction = "Right", position = new[] { 100, 100 }, property_arrangement = "左,0°" } };
        }
        else
        {
            tool = "eplan_device_place";
            request["library"] = "IEC_symbol";
            request["symbol"] = "K";
            request["symbol_variant"] = 0;
            request["tag"] = "-R10K";
            request["x"] = 100;
            request["y"] = 100;
            request["property_arrangement"] = "左,0°";
        }
        TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
        (bool ok, JsonElement result) = await eplan.CallOrErrorAsync(tool, request);
        TestContext.Current.TestOutputHelper.WriteLine("result: " + result.GetRawText());
        JsonElement readback = await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true });
        TestContext.Current.TestOutputHelper.WriteLine("readback: " + readback.GetRawText());
        Assert.True(ok, result.GetRawText());
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        JsonElement values = interruption ? Assert.Single(result.GetProperty("symbols").EnumerateArray()) : result;
        Assert.Equal("左,0°", values.GetProperty("propertyArrangement").GetString());
        Assert.Equal("左,0°", values.GetProperty("activePropertySet").GetString());
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
    }
}

using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PartVariantPlacementTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task Pxc3211813_OnlyVariant_ReadsTemplateSymbol_OneUndoRestores()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = (await session.CreatePagesAsync(1))[0];
        string before = (await session.SnapshotAsync(page)).Hash;
        var request = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["part"] = "PXC.3211813",
            ["symbol_variant"] = 1,
            ["x"] = 100,
            ["y"] = 100,
            ["dry_run"] = false,
            ["layout_guard"] = "warn"
        };
        TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
        (bool ok, JsonElement result) = await eplan.CallOrErrorAsync("eplan_device_place", request);
        TestContext.Current.TestOutputHelper.WriteLine("result: " + result.GetRawText());
        JsonElement readback = await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true });
        TestContext.Current.TestOutputHelper.WriteLine("readback: " + readback.GetRawText());
        Assert.True(ok, result.GetRawText());
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal("PXC.3211813", result.GetProperty("part").GetString());
        Assert.Equal(1, result.GetProperty("symbolVariant").GetInt32());
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
    }
}

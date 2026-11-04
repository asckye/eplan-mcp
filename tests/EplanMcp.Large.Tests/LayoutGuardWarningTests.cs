using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class LayoutGuardWarningTests(LiveEplan eplan, WriteHost host, ITestOutputHelper output) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task ReproducedLineAcrossSymbol_Warns_DefaultAndOffPageRefuse()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        async Task<PageDrawingGeometry> Read()
        {
            var geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
                new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
            output.WriteLine("readback: " + JsonSerializer.Serialize(geometry));
            return geometry;
        }
        async Task<JsonElement> Call(string tool, Dictionary<string, object?> request, bool failure = false)
        {
            output.WriteLine("request: " + JsonSerializer.Serialize(new { tool, request }));
            try
            {
                JsonElement result = failure ? await host.ErrorAsync(tool, request) : await host.CallAsync(tool, request);
                output.WriteLine("result: " + result);
                await Read();
                return result;
            }
            catch (Exception ex) { output.WriteLine(ex.ToString()); await Read(); throw; }
        }
        await Call("eplan_device_place", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["tag"] = "-LG" + Guid.NewGuid().ToString("N")[..8],
            ["x"] = 80,
            ["y"] = 140,
            ["dry_run"] = false
        });
        PageDrawingGeometry before = await Read();
        var request = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["snap_to_grid"] = false,
            ["items"] = new[] { new { kind = "line", points = (double[][])[[40, 140], [140, 140]] } }
        };
        JsonElement refused = await Call("eplan_placement_create", request, failure: true);
        Assert.Equal(ErrorReason.InvalidArgument, refused.GetProperty("reason").GetString());
        Assert.Matches(@"R[1-9]", refused.GetProperty("message").GetString());
        request["layout_guard"] = "warn";
        JsonElement committed = await Call("eplan_placement_create", request);
        Assert.Equal(ExecutionState.Committed, committed.GetProperty("execution").GetString());
        Assert.Matches(@"R[1-9]", committed.GetProperty("warning").GetString());
        Assert.Equal(before.Placements.Count + 1, (await Read()).Placements.Count);
        await session.UndoAsync();
        Assert.Equal(before.Placements.Count, (await Read()).Placements.Count);
        request["items"] = new[] { new { kind = "line", points = (double[][])[[40, 140], [before.PageSize![0] + 20, 140]] } };
        JsonElement offPage = await Call("eplan_placement_create", request, failure: true);
        Assert.Equal(ErrorReason.InvalidArgument, offPage.GetProperty("reason").GetString());
        Assert.Equal(before.Placements.Count, (await Read()).Placements.Count);
    }
}

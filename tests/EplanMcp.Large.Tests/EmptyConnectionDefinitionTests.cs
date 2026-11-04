using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class EmptyConnectionDefinitionTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly int[] _point = [80, 120];
    private static readonly double[] _expectedPoint = [80d, 120d];

    [Fact]
    public async Task DefinitionPointWithoutProperties_ReadsBack_OneUndoRestores()
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
        for (int i = 0; i < 2; i++)
        {
            await Call("eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["dry_run"] = false,
                ["terminal_strip"] = "=GAA+A1-XR4E" + page[(page.LastIndexOf('/') + 1)..],
                ["terminal_number"] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["symbol_variant"] = 0,
                ["x"] = 80,
                ["y"] = i == 0 ? 200 : 80
            });
        }
        await Call("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = new[] { page }, ["dry_run"] = false });
        PageDrawingGeometry before = await Drawing();
        string snapshot = (await session.SnapshotAsync(page)).Hash;
        JsonElement result = await Call("eplan_connection_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["response_format"] = "detailed",
            ["items"] = new[] { new { kind = "definition_point", position = _point } }
        });
        JsonElement point = Assert.Single(result.GetProperty("definitionPoints").EnumerateArray());
        Assert.Equal(1, result.GetProperty("undoSteps").GetInt32());
        Assert.Equal("ConnectionDefinitionPoint", point.GetProperty("type").GetString());
        Assert.Equal("CDP", point.GetProperty("symbol").GetProperty("name").GetString());
        Assert.Equal(_expectedPoint, point.GetProperty("position").EnumerateArray().Select(p => p.GetDouble()));
        Assert.Single((await Drawing()).Placements, p => p.Kind == "ConnectionDefinitionPoint");
        await session.UndoAsync();
        Assert.Equal(snapshot, (await session.SnapshotAsync(page)).Hash);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Drawing()));
    }
}

using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PotentialEngineeringTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task FourFields_ReadBackInOneTransaction_OneUndoRestores()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = (await session.CreatePagesAsync(1))[0];
        string before = (await session.SnapshotAsync(page)).Hash;
        var request = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["items"] = new[] { new { kind = "potential_definition", position = new[] { 100, 100 }, potential_name = "N", potential_value = "N", potential_type = 2, color = "BU", cross_section = "1.5" } },
        };
        TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
        (bool ok, JsonElement result) = await eplan.CallOrErrorAsync("eplan_connection_create", request);
        TestContext.Current.TestOutputHelper.WriteLine("result: " + result.GetRawText());
        await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PageDrawingGeometry drawing = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper.WriteLine("readback: " + JsonSerializer.Serialize(drawing));
        JsonElement found = await eplan.CallAsync("eplan_object_find", new() { ["project"] = session.Project, ["page"] = page, ["object_type"] = "PotentialDefinition" });
        TestContext.Current.TestOutputHelper.WriteLine("object readback: " + found.GetRawText());
        var values = new Dictionary<int, JsonElement>();
        if (found.GetProperty("items").GetArrayLength() == 1)
        {
            var read = new Dictionary<string, object?> { ["object_type"] = "PotentialDefinition", ["locator"] = found.GetProperty("items")[0].GetProperty("locator"), ["limit"] = 200 };
            int blocks = 0;
            do
            {
                JsonElement propertiesReadback = await eplan.CallAsync("eplan_object_get", read);
                TestContext.Current.TestOutputHelper.WriteLine("native properties: " + propertiesReadback.GetRawText());
                foreach (JsonElement row in propertiesReadback.GetProperty("items").EnumerateArray())
                {
                    int id = row.GetProperty("property_id").GetInt32();
                    if (id is 33003 or 31006 or 31004 or 31002)
                    { values[id] = row.GetProperty("value").Clone(); }
                }
                read["cursor"] = propertiesReadback.GetProperty("cursor").GetString();
                Assert.InRange(++blocks, 1, 100);
            } while (read["cursor"] is not null && values.Count < 4);
        }
        Assert.True(ok, result.GetRawText());
        JsonElement point = Assert.Single(result.GetProperty("definitionPoints").EnumerateArray());
        JsonElement properties = point.GetProperty("properties");
        Assert.Equal("N", properties.GetProperty("potential_value").GetString());
        Assert.Equal(2, properties.GetProperty("potential_type").GetInt32());
        Assert.Equal("BU", properties.GetProperty("color").GetString());
        Assert.Equal("1.5", properties.GetProperty("cross_section").GetString());
        Assert.Equal("N", values[33003].GetString());
        Assert.Equal(2, values[31006].GetInt32());
        Assert.Equal("BU", values[31004].GetString());
        Assert.Equal("1.5", values[31002].GetString());
        Assert.Single(drawing.Placements, p => p.Kind == "PotentialDefinition");
        Assert.Equal(1, result.GetProperty("undoSteps").GetInt32());
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
    }
}

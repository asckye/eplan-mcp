using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementRotationRound4Tests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task EqualCircleAnglesAndFromLayerText_ReadBackAndUndoTogether()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        string page = Assert.Single(await selection.CreatePagesAsync(1));
        var request = Request(page,
            new { kind = "arc", center = (double[])[80, 60], radius_mm = 8, start_angle = 180, end_angle = 180 },
            new { kind = "text", position = (double[])[30, 40], height_mm = 3.5, angle = -16002, contents = new Dictionary<string, string> { ["??_??"] = "From layer" } });
        JsonElement rows = (await CreateAndRead(request)).Readback.GetProperty("drawings").GetProperty("items");
        JsonElement circle = Assert.Single(rows.EnumerateArray(), r => r.GetProperty("kind").GetString() == "Arc").GetProperty("properties");
        Assert.Equal("Circle", circle.GetProperty("arc_kind").GetString());
        Assert.Equal(180, Number(circle, "start_angle"), 8);
        Assert.Equal(180, Number(circle, "end_angle"), 8);
        JsonElement text = Assert.Single(rows.EnumerateArray(), r => r.GetProperty("kind").GetString() == "Text");
        Assert.Equal("from_layer", text.GetProperty("properties").GetProperty("angle").GetString());
        await selection.UndoAsync();
        Assert.Empty((await Read(page)).GetProperty("drawings").GetProperty("items").EnumerateArray());
    }

    private Dictionary<string, object?> Request(string page, params object[] items) => new()
    {
        ["project"] = host.Writable,
        ["page"] = page,
        ["items"] = items,
        ["dry_run"] = false,
        ["snap_to_grid"] = false,
        ["response_format"] = "detailed"
    };

    private Task<JsonElement> Read(string page) => host.CallAsync("eplan_page_get", new()
    {
        ["project"] = host.Writable,
        ["page"] = page,
        ["include_drawings"] = true,
        ["drawing_limit"] = 200,
        ["response_format"] = "detailed"
    });

    private async Task<(JsonElement Result, JsonElement Readback)> CreateAndRead(Dictionary<string, object?> request)
    {
        Evidence("request", request);
        try
        {
            JsonElement result = await host.CallAsync("eplan_placement_create", request);
            Evidence("result", result);
            JsonElement readback = await Read((string)request["page"]!);
            Evidence("readback", readback);
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            return (result, readback);
        }
        catch (Exception ex)
        {
            Evidence("failed result", ex.ToString());
            Evidence("readback after failure", await Read((string)request["page"]!));
            throw;
        }
    }

    private static double Number(JsonElement properties, string name) => double.Parse(properties.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}

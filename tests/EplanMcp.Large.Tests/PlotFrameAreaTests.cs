using System.Text.Json;
namespace EplanMcp.Large.Tests;

/// <summary>
/// A145 on the fixture, read only: the circuit pages report the A3 frame's evaluation grid as their drawing area, and the
/// official drawing on them stays inside it, so off_area finds nothing there.
/// </summary>
[Trait("Size", "Large")]
public sealed class PlotFrameAreaTests(LiveEplan eplan, ITestOutputHelper output)
{
    private static readonly string[] _offArea = ["off_area"];

    [Fact]
    public async Task CircuitPages_ReportTheFrameGrid_AndTheOfficialDrawingStaysInside()
    {
        JsonElement pages = (await eplan.CallAsync("eplan_page_list", new() { ["project"] = eplan.ProjectPath, ["page_type"] = "Circuit", ["limit"] = 5 })).GetProperty("pages");
        Assert.NotEmpty(pages.EnumerateArray());
        foreach (string page in pages.EnumerateArray().Select(p => p.GetProperty("name").GetString()!))
        {
            JsonElement drawings = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 1 })).GetProperty("drawings");
            output.WriteLine(page + " area " + drawings.GetProperty("drawingArea").GetRawText());
            Assert.Equal([4.0, 16, 416, 292], drawings.GetProperty("drawingArea").EnumerateArray().Select(v => Math.Round(v.GetDouble(), 6)));

            JsonElement review = await eplan.CallAsync("eplan_page_review", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["checks"] = _offArea, ["limit"] = 20 });
            output.WriteLine(page + " review " + review.GetProperty("issues").GetRawText());
            Assert.Empty(review.GetProperty("issues").EnumerateArray());
        }
    }
}

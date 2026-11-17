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

    /// <summary>The official =EC1+A3&amp;EFS1/1 is 630 x 445.5 with the A3 frame: the grid scales with the page.</summary>
    [Fact]
    public async Task ScaledPage_ScalesTheFrameGrid_AndItsDrawingStaysInside()
    {
        const string page = "=EC1+A3&EFS1/1";
        JsonElement drawings = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 1 })).GetProperty("drawings");
        output.WriteLine(page + " page " + drawings.GetProperty("pageSize").GetRawText() + " area " + drawings.GetProperty("drawingArea").GetRawText());
        double[] area = [.. drawings.GetProperty("drawingArea").EnumerateArray().Select(v => v.GetDouble())];
        double[] size = [.. drawings.GetProperty("pageSize").EnumerateArray().Select(v => v.GetDouble())];
        Assert.Equal([6.0, 24, 624, 438], area.Select((v, i) => Math.Round(v / (i % 2 == 0 ? size[0] / 630 : size[1] / 445.5), 1)));
        JsonElement review = await eplan.CallAsync("eplan_page_review", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["checks"] = _offArea, ["limit"] = 20 });
        output.WriteLine(page + " review " + review.GetProperty("issues").GetRawText());
        // One invisible wire point at (8, -4) is off the page; it warns, nothing on this page is an error.
        Assert.DoesNotContain(review.GetProperty("issues").EnumerateArray(), i => i.GetProperty("severity").GetString() == "error");
    }
}

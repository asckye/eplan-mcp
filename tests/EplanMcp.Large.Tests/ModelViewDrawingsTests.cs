using System.Text.Json;
namespace EplanMcp.Large.Tests;

/// <summary>
/// Live 2026-11-16, read only on the fixture: the drawings of these model view pages passed the 16 MiB reply limit when
/// the Bridge sent the whole page. With the slice request the first two pages of objects read, with the page total.
/// </summary>
[Trait("Size", "Large")]
public sealed class ModelViewDrawingsTests(LiveEplan eplan, ITestOutputHelper output)
{
    [Theory]
    [InlineData("=GQA+B2.Y1&MTC1/1")]
    [InlineData("=K2+B2.Y1&MTC1/1")]
    [InlineData("=K2+B2.Y1&MTC1/4")]
    public async Task LargeModelViewPage_ReadsInSlices(string page)
    {
        JsonElement first = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200 })).GetProperty("drawings");
        int total = first.GetProperty("total").GetInt32();
        output.WriteLine(page + ": total " + total + ", first " + first.GetProperty("items").GetArrayLength());
        Assert.True(total > 200, "expected a large page");
        string cursor = first.GetProperty("nextCursor").GetString()!;
        JsonElement second = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["drawing_cursor"] = cursor })).GetProperty("drawings");
        Assert.Equal(total, second.GetProperty("total").GetInt32());
        long[] Ids(JsonElement d) => [.. d.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("locator").GetProperty("oid").GetInt64())];
        Assert.NotEmpty(Ids(second));
        output.WriteLine(page + ": second " + Ids(second).Length);
    }
}

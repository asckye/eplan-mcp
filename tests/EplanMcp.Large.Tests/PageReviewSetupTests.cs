using System.Text.Json;
namespace EplanMcp.Large.Tests;

/// <summary>
/// Batch 2 (owner 2026-11-16), read only on the fixture: the official circuit pages keep devices and their pins in one
/// column and share frame and grid, so path_straddle and page_setup find nothing there.
/// </summary>
[Trait("Size", "Large")]
public sealed class PageReviewSetupTests(LiveEplan eplan, ITestOutputHelper output)
{
    private static readonly string[] _checks = ["path_straddle", "page_setup"];

    [Fact]
    public async Task OfficialCircuitPages_HaveNoColumnOrSetupFindings()
    {
        JsonElement pages = (await eplan.CallAsync("eplan_page_list", new() { ["project"] = eplan.ProjectPath, ["page_type"] = "Circuit", ["limit"] = 12 })).GetProperty("pages");
        var found = new List<string>();
        foreach (string page in pages.EnumerateArray().Select(p => p.GetProperty("name").GetString()!))
        {
            JsonElement review = await eplan.CallAsync("eplan_page_review", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["checks"] = _checks, ["limit"] = 50 });
            foreach (JsonElement issue in review.GetProperty("issues").EnumerateArray())
            {
                found.Add(page + " " + issue.GetProperty("code").GetString());
                string raw = issue.GetRawText();
                output.WriteLine(page + " " + raw[..Math.Min(500, raw.Length)]);
            }
        }
        Assert.Empty(found);
    }
}

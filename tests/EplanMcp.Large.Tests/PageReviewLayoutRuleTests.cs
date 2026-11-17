using System.Text.Json;
namespace EplanMcp.Large.Tests;

/// <summary>
/// The write-time layout rules in eplan_page_review (owner 2026-11-16), read only on the fixture: the official circuit pages
/// carry no errors from them.
/// </summary>
[Trait("Size", "Large")]
public sealed class PageReviewLayoutRuleTests(LiveEplan eplan, ITestOutputHelper output)
{
    private static readonly string[] _checks = ["overlaps", "wire_through_symbol", "net_overlap", "text_format", "cross_reference_clash"];

    [Fact]
    public async Task OfficialCircuitPages_HaveNoLayoutErrors()
    {
        JsonElement pages = (await eplan.CallAsync("eplan_page_list", new() { ["project"] = eplan.ProjectPath, ["page_type"] = "Circuit", ["limit"] = 12 })).GetProperty("pages");
        var errors = new List<string>();
        foreach (string page in pages.EnumerateArray().Select(p => p.GetProperty("name").GetString()!))
        {
            JsonElement review = await eplan.CallAsync("eplan_page_review", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["checks"] = _checks, ["limit"] = 200 });
            JsonElement[] issues = [.. review.GetProperty("issues").EnumerateArray()];
            output.WriteLine(page + ": " + string.Join(", ", issues.GroupBy(i => i.GetProperty("code").GetString() + "/" + i.GetProperty("severity").GetString()).Select(g => g.Key + " x" + g.Count())));
            foreach (JsonElement issue in issues.Where(i => i.GetProperty("severity").GetString() == "error"))
            {
                errors.Add(page + " " + issue.GetRawText());
                output.WriteLine("  " + issue.GetRawText());
            }
        }
        Assert.Empty(errors);
    }
}

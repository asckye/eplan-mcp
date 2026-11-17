using System.Text.Json;
namespace EplanMcp.Large.Tests;

/// <summary>
/// Batch 3 (owner 2026-11-16), read only on the fixture: the official circuit pages have their wires numbered and no
/// placeholder device tags, so wire_number and placeholder_tag give no warning; at least one page shows numbered wires.
/// </summary>
[Trait("Size", "Large")]
public sealed class PageReviewNumberingTests(LiveEplan eplan, ITestOutputHelper output)
{
    private static readonly string[] _checks = ["wire_number", "placeholder_tag"];

    [Fact]
    public async Task OfficialCircuitPages_AreNumberedWithoutPlaceholders()
    {
        JsonElement pages = (await eplan.CallAsync("eplan_page_list", new() { ["project"] = eplan.ProjectPath, ["page_type"] = "Circuit", ["limit"] = 12 })).GetProperty("pages");
        var warnings = new List<string>();
        int notes = 0;
        foreach (string page in pages.EnumerateArray().Select(p => p.GetProperty("name").GetString()!))
        {
            JsonElement review = await eplan.CallAsync("eplan_page_review", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["checks"] = _checks, ["limit"] = 50 });
            foreach (JsonElement issue in review.GetProperty("issues").EnumerateArray())
            {
                string raw = issue.GetRawText();
                output.WriteLine(page + " " + raw[..Math.Min(400, raw.Length)]);
                if (issue.GetProperty("severity").GetString() == "info")
                { notes++; }
                else
                { warnings.Add(page + " " + issue.GetProperty("code").GetString()); }
            }
        }
        output.WriteLine("pages without any wire number: " + notes);
        Assert.Empty(warnings);
        // Live 2026-11-16: most official circuit pages carry no wire numbers, so the check must speak on some page; a check
        // that filters every wire away stays silent everywhere.
        Assert.True(notes > 0, "wire_number said nothing on any page; it no longer sees the wires.");
    }
}

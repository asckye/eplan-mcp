using System.Diagnostics;
using System.Text.Json;
namespace EplanMcp.Large.Tests;

/// <summary>
/// Owner 2026-11-16, read only on the fixture: the whole-page layout rules stay fast on the pages with the most objects.
/// Prints object counts and review times; fails above 15 seconds, three quarters of the review's hard limit. Live
/// 2026-11-16: =EC1+A3&amp;EFS1/1 (3870 objects) takes about 9 s with any single check: reading the page (connections,
/// cables, pins) dominates, and the layout rules add under a second.
/// </summary>
[Trait("Size", "Large")]
public sealed class PageReviewSpeedTests(LiveEplan eplan, ITestOutputHelper output)
{
    private static readonly string[] _layout = ["overlaps", "off_area", "wire_through_symbol", "net_overlap", "text_format", "cross_reference_clash"];

    [Fact]
    public async Task LargestPages_ReviewWithinFifteenSeconds()
    {
        var counts = new List<(string Page, int Total)>();
        string? cursor = null;
        do
        {
            var args = new Dictionary<string, object?> { ["project"] = eplan.ProjectPath, ["limit"] = 200 };
            if (cursor is not null) { args["cursor"] = cursor; }
            JsonElement list = await eplan.CallAsync("eplan_page_list", args);
            foreach (string page in list.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()!))
            {
                try
                {
                    JsonElement d = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 1 })).GetProperty("drawings");
                    counts.Add((page, d.GetProperty("total").GetInt32()));
                }
                // Live 2026-11-16: the drawings of the model view page =GQA+B2.Y1&MTC1/1 pass the reply limit; they are skipped.
                catch (Exception ex) when (ex.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.Ordinal))
                { output.WriteLine(page + ": " + ex.Message[..Math.Min(200, ex.Message.Length)]); }
            }
            cursor = list.TryGetProperty("nextCursor", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        } while (cursor is not null);

        var slow = new List<string>();
        foreach ((string page, int total) in counts.OrderByDescending(c => c.Total).Take(6))
        {
            var watch = Stopwatch.StartNew();
            JsonElement review = await eplan.CallAsync("eplan_page_review", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["checks"] = _layout, ["limit"] = 1 });
            watch.Stop();
            output.WriteLine(FormattableString.Invariant($"{page}: {total} objects, {watch.ElapsedMilliseconds} ms, summary {review.GetProperty("summary").GetRawText()}"));
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) { slow.Add(page); }
        }
        Assert.Empty(slow);
    }
}

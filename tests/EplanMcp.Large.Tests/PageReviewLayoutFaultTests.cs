using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Owner 2026-11-16: faults drawn on purpose with layout_guard "warn" on a new LargeWrite page are reported by
/// eplan_page_review: a symbol on another symbol (error), a line in the title block (off_area error) and a slanted text
/// (text_format warning). Undone afterwards.
/// </summary>
[Trait("Size", "Large")]
public sealed class PageReviewLayoutFaultTests(LiveEplan eplan, WriteHost host, ITestOutputHelper output) : IClassFixture<WriteHost>
{
    private static readonly string[] _checks = ["overlaps", "off_area", "text_format"];

    [Fact]
    public async Task DeliberateLayoutFaults_AreReported()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        async Task<JsonElement> Call(string tool, Dictionary<string, object?> request)
        {
            JsonElement result = await host.CallAsync(tool, request);
            output.WriteLine(tool + ": " + result.GetRawText()[..Math.Min(600, result.GetRawText().Length)]);
            return result;
        }
        Dictionary<string, object?> Device(string tag, double x) => new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["tag"] = tag,
            ["x"] = x,
            ["y"] = 140,
            ["dry_run"] = false,
            ["layout_guard"] = "warn",
        };
        string id = Guid.NewGuid().ToString("N")[..6];
        await Call("eplan_device_place", Device("-RA" + id, 80));
        await Call("eplan_device_place", Device("-RB" + id, 84));
        await Call("eplan_placement_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["layout_guard"] = "warn",
            ["items"] = new object[]
            {
                new { kind = "line", points = (double[][])[[200, 8], [240, 8]] },
                new { kind = "text", position = (double[])[200, 200], contents = new Dictionary<string, string> { ["??_??"] = "slanted" }, height_mm = 3.5, angle = 45 },
            },
        });

        JsonElement review = await Call("eplan_page_review", new() { ["project"] = session.Project, ["page"] = page, ["checks"] = _checks, ["limit"] = 50 });
        JsonElement[] issues = [.. review.GetProperty("issues").EnumerateArray()];
        Assert.Contains(issues, i => i.GetProperty("code").GetString() == "overlap" && i.GetProperty("severity").GetString() == "error");
        Assert.Contains(issues, i => i.GetProperty("code").GetString() == "off_area" && i.GetProperty("severity").GetString() == "error");
        Assert.Contains(issues, i => i.GetProperty("code").GetString() == "text_format" && i.GetProperty("severity").GetString() == "warning");
        await session.UndoAsync();
        await session.UndoAsync();
        await session.UndoAsync();
    }
}

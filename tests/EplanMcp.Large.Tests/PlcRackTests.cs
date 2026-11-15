using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_plc_rack on the write copy (docs/tools/eplan_plc_rack.md, section 9; large-pending.md). Two ET 200SP cards from
/// the configured parts database are drawn on new pages after the last one of =GAA+A1&amp;EFS1: each overview on its own
/// new Overview page, the pictures side by side on a new Circuit page. What the run must show live: renaming freshly
/// inserted devices and joining the picture to the overview inside one transaction, adding the parts there, and the
/// part data transfer after the commit; then one undo restores the copy. A database without ET 200SP parts that name
/// a window macro skips the test with that reason; it does not count as passed.
/// </summary>
[Trait("Size", "Large")]
public sealed class PlcRackTests(LiveEplan eplan) : IClassFixture<WriteHost>
{
    private const string Prefix = "=GAA+A1&EFS1/";

    [Fact]
    public async Task TwoCards_AreOneDeviceEachWithTheirParts_OnBothPages_OneUndoRestores()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string project = selection.Project;
        string[] parts = await CardPartsAsync();
        int first = await LastPageAsync(project) + 1;
        string rack = Page(first), overview1 = Page(first + 1), overview2 = Page(first + 2);
        LiveSnapshots.Snapshot before = await selection.SnapshotAsync();
        await CallAsync("eplan_page_create", new()
        {
            ["project"] = project,
            ["dry_run"] = false,
            ["pages"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { name = rack },
                new { name = overview1, page_type = "Overview" },
                new { name = overview2, page_type = "Overview" },
            }),
        });
        string stamp = first.ToString(CultureInfo.InvariantCulture);
        string[] tags = ["=GAA+A1-KR" + stamp + "A", "=GAA+A1-KR" + stamp + "B"];
        Dictionary<string, object?> args = new()
        {
            ["project"] = project,
            ["rack_page"] = rack,
            ["cards"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { tag = tags[0], part = parts[0], overview_page = overview1, picture_variant = 5 },
                new { tag = tags[1], part = parts[1], overview_page = overview2, picture_variant = 5 },
            }),
        };

        JsonElement preview = await CallAsync("eplan_plc_rack", args);
        Assert.Equal(2, preview.GetProperty("changes").GetArrayLength());
        args["dry_run"] = false;
        JsonElement drawn = await CallAsync("eplan_plc_rack", args);

        Assert.Equal(ExecutionState.Committed, drawn.GetProperty("execution").GetString());
        for (int i = 0; i < 2; i++)
        {
            JsonElement card = drawn.GetProperty("cards")[i];
            Assert.Equal(tags[i], card.GetProperty("tag").GetString());
            Assert.Contains(parts[i], card.GetProperty("parts").EnumerateArray().Select(p => p.GetString()));
            JsonElement device = await CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tags[i] });
            string[] pages = [.. device.GetProperty("functions").EnumerateArray().Select(f => f.GetProperty("page").GetString() ?? string.Empty).Distinct()];
            Assert.Contains(rack, pages);
            Assert.Contains(i == 0 ? overview1 : overview2, pages);
        }

        // The pages were created by a separate write: one undo takes the rack back, a second the pages.
        await selection.UndoAsync();
        await selection.UndoAsync();
        Assert.Equal(before.Hash, (await selection.SnapshotAsync()).Hash);
    }

    /// <summary>Two ET 200SP digital cards that name a window macro, from the configured parts database.</summary>
    private async Task<string[]> CardPartsAsync()
    {
        JsonElement found = await CallAsync("eplan_part_search", new() { ["query"] = "SIE.6ES713", ["limit"] = 50 });
        string[] candidates = [.. found.GetProperty("parts").EnumerateArray()
            .Select(p => p.GetProperty("partNumber").GetString()!)
            .Where(p => p.StartsWith("SIE.6ES7131-6", StringComparison.Ordinal) || p.StartsWith("SIE.6ES7132-6", StringComparison.Ordinal))
            .Distinct()];
        if (candidates.Length < 2)
        {
            Assert.Skip("The configured parts database has fewer than two ET 200SP DI/DQ parts (SIE.6ES7131-6/6ES7132-6); eplan_plc_rack is not verified by this run.");
        }

        return candidates[..2];
    }

    private async Task<int> LastPageAsync(string project)
    {
        int last = 0;
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = new() { ["project"] = project, ["name_prefix"] = Prefix, ["limit"] = 200 };
            if (cursor is not null)
            { args["cursor"] = cursor; }
            JsonElement list = await eplan.CallAsync("eplan_page_list", args);
            foreach (JsonElement page in list.GetProperty("pages").EnumerateArray())
            {
                if (int.TryParse(page.GetProperty("name").GetString()![Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                { last = Math.Max(last, number); }
            }

            cursor = list.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);
        return last;
    }

    private static string Page(int number) => Prefix + number.ToString(CultureInfo.InvariantCulture);

    private async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> args)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(tool + " request: " + JsonSerializer.Serialize(args));
        (bool succeeded, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
        TestContext.Current.TestOutputHelper.WriteLine(tool + " result: " + result.GetRawText());
        Assert.True(succeeded, tool + " failed: " + result.GetRawText());
        return result;
    }
}

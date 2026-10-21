using System.Globalization;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_macro_insert on the write copy (README.md), never on the read tests' fixture (docs/tools/eplan_macro_insert.md,
/// section 7). Each test adds new pages to =GAA+A1&amp;EFS1 after the last one there, so runs do not collide; the copy
/// keeps what earlier runs inserted, and the tests allow for that.
/// </summary>
[Trait("Size", "Large")]
public sealed class MacroInsertTests(LiveEplan eplan)
{
    private const string Prefix = "=GAA+A1&EFS1/";

    [Fact]
    public async Task Number_OnANewPage_InsertsTheMacroOnce_AndTheSamePointAgainIsRolledBack()
    {
        string project = await eplan.WriteProjectAsync();
        string page = (await NewPagesAsync(project, 1))[0];
        Dictionary<string, object?> args = Args(project, page, "number");

        JsonElement dryRun = await eplan.CallAsync("eplan_macro_insert", new(args) { ["dry_run"] = true });
        JsonElement inserted = await eplan.CallAsync("eplan_macro_insert", args);
        int onPage = await DevicesOnAsync(project, page);
        JsonElement stacked = await eplan.ErrorAsync("eplan_macro_insert", args);

        // The plan opens the macro without inserting it (A14): its own placeholder tags, 28 objects (S9, S10).
        Assert.Equal(("not_started", 28), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("objectCount").GetInt32()));
        Assert.Contains(dryRun.GetProperty("devices").EnumerateArray(), d => d.GetProperty("tag").GetString() == "+-WG6");

        // S10 (A14): the devices take the page's structure and show their tags; the cable and the terminal strips get
        // tags no device has. A page from eplan_page_create already has its size (api-notes A20), so the outline was
        // checked against it; no hint about an unknown size.
        Assert.Equal(("committed", "AI: eplan_macro_insert", 28), (inserted.GetProperty("execution").GetString(), inserted.GetProperty("undo").GetString(), inserted.GetProperty("objectCount").GetInt32()));
        JsonElement[] devices = [.. inserted.GetProperty("devices").EnumerateArray()];
        JsonElement cable = Assert.Single(devices, d => d.GetProperty("category").GetString() == "Cable");
        Assert.StartsWith("+A1-WG", cable.GetProperty("tag").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("-WG", cable.GetProperty("visibleTag").GetString(), StringComparison.Ordinal);
        Assert.Contains(devices, d => d.GetProperty("tag").GetString()!.StartsWith("+A1-XG", StringComparison.Ordinal));
        Assert.DoesNotContain("only x >= 0 and y >= 0 were checked", inserted.GetProperty("hint").GetString() ?? string.Empty, StringComparison.Ordinal);
        Assert.InRange(onPage, 1, 50);

        // The same macro at the same point: EPLAN refuses to stack the same symbols ("S029181", A13), nothing stays.
        Assert.Equal(("INVALID_ARGUMENT", "rolled_back"), (stacked.GetProperty("reason").GetString(), stacked.GetProperty("execution").GetString()));
        Assert.Equal(onPage, await DevicesOnAsync(project, page));
    }

    [Fact]
    public async Task Keep_WhereTheMacrosTagsAreInUse_IsAlreadyExists_AndLeavesTheProjectAsItWas()
    {
        string project = await eplan.WriteProjectAsync();
        string[] pages = await NewPagesAsync(project, 2);

        // The first keep insert gives the project the macro's own tags (+A1-WG6, S10), unless an earlier run did.
        (bool first, JsonElement firstResult) = await eplan.CallOrErrorAsync("eplan_macro_insert", Args(project, pages[0], "keep"));
        Assert.True(first || firstResult.GetProperty("reason").GetString() == "ALREADY_EXISTS", firstResult.ToString());
        (int Pages, int Devices, int Connections, int Parts) before = await CountsAsync(project);

        JsonElement merged = await eplan.ErrorAsync("eplan_macro_insert", Args(project, pages[1], "keep"));

        // S10: None gave the project a second main function +A1-WG6; the tool rolls that back (tool spec 4).
        Assert.Equal(("ALREADY_EXISTS", "rolled_back"), (merged.GetProperty("reason").GetString(), merged.GetProperty("execution").GetString()));
        Assert.Contains("+A1-WG6", merged.GetProperty("metadata").GetProperty("tags").GetString()!.Split('\n'));
        Assert.Equal(before, await CountsAsync(project));
        Assert.Equal(0, await DevicesOnAsync(project, pages[1]));
    }

    private static Dictionary<string, object?> Args(string project, string page, string numbering) => new()
    {
        ["project"] = project,
        ["page"] = page,
        ["macro"] = MacroTests.FeedbackControl,
        ["x"] = 40,
        ["y"] = 280,
        ["numbering"] = numbering,
    };

    /// <summary>New circuit pages after the last numbered page of =GAA+A1&amp;EFS1 in the copy, created in one call.</summary>
    private async Task<string[]> NewPagesAsync(string project, int count)
    {
        int last = 0;
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = new() { ["project"] = project, ["name_prefix"] = Prefix, ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }

            JsonElement list = await eplan.CallAsync("eplan_page_list", args);
            foreach (JsonElement page in list.GetProperty("pages").EnumerateArray())
            {
                if (int.TryParse(page.GetProperty("name").GetString()![Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    last = Math.Max(last, number);
                }
            }

            cursor = list.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        string[] names = [.. Enumerable.Range(last + 1, count).Select(n => Prefix + n.ToString(CultureInfo.InvariantCulture))];
        await eplan.CallAsync("eplan_page_create", new() { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(names.Select(name => new { name })) });
        return names;
    }

    private async Task<int> DevicesOnAsync(string project, string page) =>
        (await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page })).GetProperty("deviceCount").GetInt32();

    private async Task<(int Pages, int Devices, int Connections, int Parts)> CountsAsync(string project)
    {
        JsonElement counts = (await eplan.CallAsync("eplan_project_get", new() { ["project"] = project })).GetProperty("counts");
        return (counts.GetProperty("pages").GetInt32(), counts.GetProperty("devices").GetInt32(), counts.GetProperty("connections").GetInt32(), counts.GetProperty("partReferences").GetInt32());
    }
}

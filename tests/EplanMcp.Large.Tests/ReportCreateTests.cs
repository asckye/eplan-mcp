using System.Globalization;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>A84 report settings and generation through the public tools; all writes target LargeWrite.</summary>
[Trait("Size", "Large")]
public sealed class ReportCreateTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task SampleReportSettings_CreateDirectoryAndTerminalDiagram_ThenUndoRestoresPageList()
    {
        JsonElement sample = await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = "&AAB1/1" });
        JsonElement report = sample.GetProperty("report");
        Assert.Equal("TableOfContents", report.GetProperty("type").GetString());
        string type = report.GetProperty("type").GetString()!;
        string form = report.GetProperty("form").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(form));
        JsonElement terminalSample = await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = "+A1&EMA2/2" });
        JsonElement terminalReport = terminalSample.GetProperty("report");
        Assert.Equal(("TerminalDiagram", "F13_008", "Location designation_A3"),
            (terminalReport.GetProperty("type").GetString(), terminalReport.GetProperty("form").GetString(), terminalReport.GetProperty("filter_scheme").GetString()));
        Assert.False(string.IsNullOrEmpty(terminalReport.GetProperty("filter_scheme_data").GetString()));

        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project);
        string[] before = await PageNamesAsync(session);
        string directoryFirst = FirstFreePage(before, "&AAB1/");
        Dictionary<string, object?> directory = new()
        {
            ["project"] = session.Project,
            ["type"] = type,
            ["form"] = form,
            ["first_page"] = directoryFirst,
            ["dry_run"] = false,
        };
        JsonElement preview = await host.CallAsync("eplan_report_create", new(directory) { ["dry_run"] = true });
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        Assert.Equal(before, await PageNamesAsync(session));
        JsonElement created = await host.CallAsync("eplan_report_create", directory);
        await AssertGeneratedAsync(session, created, type, form, before);

        string[] afterDirectory = await PageNamesAsync(session);
        Dictionary<string, object?> terminalArgs = new()
        {
            ["project"] = session.Project,
            ["type"] = "TerminalDiagram",
            ["form"] = "F13_008",
            ["first_page"] = FirstFreePage(afterDirectory, "+A1&EMA2/"),
            ["dry_run"] = false,
        };
        foreach (string name in new[] { "filter_scheme", "sort_scheme" })
        {
            string? value = terminalReport.GetProperty(name).GetString();
            if (!string.IsNullOrEmpty(value))
            {
                terminalArgs[name] = value;
            }
        }
        foreach (string name in new[] { "filter_scheme_data", "sort_scheme_data", "function_filter_scheme_data", "function_sort_scheme_data" })
        {
            terminalArgs[name] = terminalReport.GetProperty(name).GetString();
        }
        JsonElement terminals = await host.CallAsync("eplan_report_create", terminalArgs);
        await AssertGeneratedAsync(session, terminals, "TerminalDiagram", "F13_008", afterDirectory);
        await session.UndoAsync();
        Assert.Equal(afterDirectory, await PageNamesAsync(session));
        await session.UndoAsync();
        Assert.Equal(before, await PageNamesAsync(session));
    }

    private async Task AssertGeneratedAsync(PlanStepLiveSession session, JsonElement result, string type, string form, string[] before)
    {
        Assert.Equal(("committed", "AI: eplan_report_create", type, form),
            (result.GetProperty("execution").GetString(), result.GetProperty("undo").GetString(), result.GetProperty("type").GetString(), result.GetProperty("form").GetString()));
        JsonElement.ArrayEnumerator pages = result.GetProperty("pages").EnumerateArray();
        Assert.Equal(result.GetProperty("count").GetInt32(), pages.Count());
        Assert.True(result.GetProperty("count").GetInt32() > 0);
        string[] newPages = [.. result.GetProperty("new_pages").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal)];
        Assert.Equal((await PageNamesAsync(session)).Except(before, StringComparer.Ordinal).Order(StringComparer.Ordinal), newPages);
        foreach (JsonElement page in pages)
        {
            JsonElement read = await host.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page.GetString() });
            JsonElement block = read.GetProperty("report");
            Assert.Equal((type, form), (block.GetProperty("type").GetString(), block.GetProperty("form").GetString()));
        }
    }

    private static async Task<string[]> PageNamesAsync(PlanStepLiveSession session) =>
        [.. (await session.SnapshotAsync()).Pages.Select(p => p.Name).Order(StringComparer.Ordinal)];

    private static string FirstFreePage(IEnumerable<string> pages, string prefix)
    {
        int max = pages.Where(p => p.StartsWith(prefix, StringComparison.Ordinal))
            .Select(p => int.TryParse(p[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0).DefaultIfEmpty().Max();
        return prefix + checked(max + 100).ToString(CultureInfo.InvariantCulture);
    }
}

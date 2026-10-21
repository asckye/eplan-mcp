using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_check_run and eplan_report_update on the write copy (README.md), never on the read tests' fixture
/// (docs/tools/eplan_check_run.md and docs/tools/eplan_report_update.md, section 7). The copy keeps what earlier runs
/// and other write tests did (new pages, updated reports), so the tests compare the tools with each other and with
/// themselves rather than with the sample project's numbers, and write what the specs leave open to the test output
/// for api-notes.
/// </summary>
[Trait("Size", "Large")]
public sealed class CheckAndReportTests(LiveEplan eplan)
{
    private const string Circuit1 = "=GAA+A1&EFS1/1";
    private const string Circuit2 = "=GAA+A1&EFS1/2";
    private const string Contents = "&AAB1/1";
    private const string NewPartsList = "+A2&EPA1/1.2";

    [Fact]
    public async Task CheckRun_OfTheProject_ReadsAsCheckList_AndAddsTheSummaryHintsAgain()
    {
        string project = await eplan.WriteProjectAsync();
        int pages = (await eplan.CallAsync("eplan_project_get", new() { ["project"] = project })).GetProperty("counts").GetProperty("pages").GetInt32();

        JsonElement dryRun = await eplan.CallAsync("eplan_check_run", new() { ["project"] = project, ["dry_run"] = true });
        JsonElement first = await eplan.CallAsync("eplan_check_run", new() { ["project"] = project });
        JsonElement listed = await eplan.CallAsync("eplan_check_list", new() { ["project"] = project, ["limit"] = 200 });
        JsonElement second = await eplan.CallAsync("eplan_check_run", new() { ["project"] = project });

        Assert.Equal(("not_started", pages, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("pageCount").GetInt32(), dryRun.GetProperty("counts").ValueKind));
        Assert.Equal(("committed", "AI: eplan_check_run", pages), (first.GetProperty("execution").GetString(), first.GetProperty("undo").GetString(), first.GetProperty("pageCount").GetInt32()));

        // S6 and S11 found 6 warnings and 14 hints on the sample project; pages other write tests added may add more.
        Assert.InRange(first.GetProperty("total").GetInt32(), 20, int.MaxValue);
        Assert.Equal(Counts(first), Counts(listed));
        Assert.Equal(first.GetProperty("total").GetInt32(), listed.GetProperty("total").GetInt32());
        Assert.Equal(Messages(first), Messages(listed).Take(first.GetProperty("messages").GetArrayLength()));

        // Checking again replaces the errors and warnings but adds EPLAN's 14 summary hints again, one per check region
        // (api-notes A20), which is why the tool is not idempotent.
        (int Error, int Warning, int Hint) before = Counts(first), after = Counts(second);
        Assert.Equal((before.Error, before.Warning, before.Hint + 14), after);
        Assert.Equal(first.GetProperty("total").GetInt32() + 14, second.GetProperty("total").GetInt32());
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"check: dry run before {dryRun.GetProperty("before").GetInt32()}; first {first.GetProperty("total").GetInt32()} messages; second run before {second.GetProperty("before").GetInt32()} (done ones included)");
    }

    [Fact]
    public async Task CheckRun_OfTwoPages_ChecksThoseOnly()
    {
        string project = await eplan.WriteProjectAsync();
        JsonElement whole = await eplan.CallAsync("eplan_check_run", new() { ["project"] = project });

        JsonElement two = await eplan.CallAsync("eplan_check_run", new() { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(new[] { Circuit1, Circuit2 }) });

        Assert.Equal(("committed", 2), (two.GetProperty("execution").GetString(), two.GetProperty("pageCount").GetInt32()));
        Assert.Equal(whole.GetProperty("total").GetInt32(), two.GetProperty("before").GetInt32());

        // Not verified (tool spec 5): whether checking pages removes the other pages' messages. Fewer messages than
        // after the whole project's check mean it does.
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"check of 2 pages: {whole.GetProperty("total").GetInt32()} messages after the whole project, {two.GetProperty("total").GetInt32()} after the two pages");
    }

    [Theory]
    [InlineData(false)] // VerifyProject: what it does with an unknown scheme is not documented
    [InlineData(true)] // VerifyPages: InvalidScheme (EPLAN XML docs)
    public async Task CheckRun_WithASchemeEplanDoesNotKnow_IsRolledBack(bool pages)
    {
        string project = await eplan.WriteProjectAsync();
        await eplan.CallAsync("eplan_check_run", new() { ["project"] = project });
        JsonElement before = await eplan.CallAsync("eplan_check_list", new() { ["project"] = project, ["include_done"] = true, ["limit"] = 1 });
        Dictionary<string, object?> args = new() { ["project"] = project, ["scheme"] = "EplanMcp no such scheme" };
        if (pages)
        {
            args["pages"] = JsonSerializer.SerializeToElement(new[] { Circuit1 });
        }

        JsonElement error = await eplan.ErrorAsync("eplan_check_run", args);
        JsonElement after = await eplan.CallAsync("eplan_check_list", new() { ["project"] = project, ["include_done"] = true, ["limit"] = 1 });

        Assert.Equal(("INVALID_ARGUMENT", "rolled_back"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal((Counts(before), before.GetProperty("total").GetInt32()), (Counts(after), after.GetProperty("total").GetInt32()));
        TestContext.Current.TestOutputHelper?.WriteLine("EPLAN's message: " + error.GetProperty("metadata").GetProperty("eplanMessage").GetString());
    }

    [Fact]
    public async Task ReportUpdate_OfAllReports_CreatesThePartsListPage_AndAgainChangesNoPage()
    {
        string project = await eplan.WriteProjectAsync();

        JsonElement dryRun = await eplan.CallAsync("eplan_report_update", new() { ["project"] = project, ["dry_run"] = true });
        JsonElement first = await eplan.CallAsync("eplan_report_update", new() { ["project"] = project });
        JsonElement second = await eplan.CallAsync("eplan_report_update", new() { ["project"] = project });

        // S11 counted 204 report pages in the sample project by page type; a copy whose reports an earlier run updated has more.
        int before = dryRun.GetProperty("reportPages").GetProperty("before").GetInt32();
        Assert.Equal(("not_started", "project", JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("scope").GetString(), dryRun.GetProperty("added").ValueKind));
        Assert.InRange(before, 204, int.MaxValue);
        Assert.Equal(("committed", "AI: eplan_report_update", before), (first.GetProperty("execution").GetString(), first.GetProperty("undo").GetString(), first.GetProperty("reportPages").GetProperty("before").GetInt32()));
        Assert.Equal(
            first.GetProperty("reportPages").GetProperty("after").GetInt32(),
            before + first.GetProperty("added").GetArrayLength() - first.GetProperty("removed").GetArrayLength());
        JsonElement partsList = await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = NewPartsList });
        Assert.Equal(NewPartsList, partsList.GetProperty("page").GetProperty("name").GetString());

        // Updating again with the data unchanged adds and removes nothing (tool spec 5: idempotentHint).
        Assert.Equal((0, 0), (second.GetProperty("added").GetArrayLength(), second.GetProperty("removed").GetArrayLength()));
        Assert.Equal(first.GetProperty("reportPages").GetProperty("after").GetInt32(), second.GetProperty("reportPages").GetProperty("after").GetInt32());
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"reports: {before} report pages before; first update added [{string.Join(", ", Names(first, "added"))}], removed [{string.Join(", ", Names(first, "removed"))}]");
    }

    [Fact]
    public async Task ReportUpdate_OfTheTableOfContents_KeepsTheReportPages()
    {
        string project = await eplan.WriteProjectAsync();

        JsonElement contents = await eplan.CallAsync("eplan_report_update", new() { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(new[] { Contents }) });

        Assert.Equal(("committed", "pages"), (contents.GetProperty("execution").GetString(), contents.GetProperty("scope").GetString()));
        Assert.Equal(contents.GetProperty("reportPages").GetProperty("before").GetInt32(), contents.GetProperty("reportPages").GetProperty("after").GetInt32());
        Assert.Equal((0, 0), (contents.GetProperty("added").GetArrayLength(), contents.GetProperty("removed").GetArrayLength()));
    }

    [Fact]
    public async Task ReportUpdate_OfACircuitPage_IsInvalid()
    {
        string project = await eplan.WriteProjectAsync();

        JsonElement error = await eplan.ErrorAsync("eplan_report_update", new() { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(new[] { Circuit1 }) });

        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Contains("pages[0] is not a report page", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
    }

    private static (int Error, int Warning, int Hint) Counts(JsonElement result)
    {
        JsonElement counts = result.GetProperty("counts");
        return (counts.GetProperty("error").GetInt32(), counts.GetProperty("warning").GetInt32(), counts.GetProperty("hint").GetInt32());
    }

    private static IEnumerable<(int Number, string? Text)> Messages(JsonElement result) =>
        result.GetProperty("messages").EnumerateArray().Select(m => (m.GetProperty("number").GetInt32(), m.GetProperty("text").GetString()));

    private static IEnumerable<string?> Names(JsonElement result, string list) => result.GetProperty(list).EnumerateArray().Select(p => p.GetString());
}

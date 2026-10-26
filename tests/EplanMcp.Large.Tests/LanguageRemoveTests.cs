using System.Diagnostics;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_language_remove on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_language_remove.md,
/// section 7). S23 removed sv_SE from the sample project: 460 texts lost their Swedish translation and the displayed
/// languages became zh_CN (api-notes A28). The first test adds a language and removes it again, so it can run every
/// time; the second removes sv_SE for good, so it runs once per copy of the sample project (README.md). What the spec
/// leaves to these tests goes to the test output: the time, and the displayed languages after the removal.
/// </summary>
[Trait("Size", "Large")]
public sealed class LanguageRemoveTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_language_remove";

    /// <summary>Languages EPLAN knows that the sample project does not have; the first one missing is added and removed.</summary>
    private static readonly string[] _toAdd = ["lt_LT", "sl_SI", "is_IS", "et_EE", "lv_LV"];

    private static readonly string[] _notTranslated = ["yy_XX", "en_EN", "so_SO"];

    private static readonly string[] _swedish = ["sv_SE"];

    private static readonly string[] _unset = ["##_##"];

    private readonly DestructiveHost _host;

    public LanguageRemoveTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task AnAddedLanguage_HasNoTranslations_AndIsRemovedAfterABackup()
    {
        JsonElement before = await SettingsAsync();
        List<string> translation = Codes(before, "translation");
        List<string> display = Codes(before, "display");
        string? language = _toAdd.FirstOrDefault(code => !translation.Contains(code));
        Assert.SkipWhen(language is null, "Every candidate language is a translation language of the copy already; copy it again (README.md).");
        await _host.CallAsync("eplan_language_set", new() { ["project"] = _host.Writable, ["add"] = DestructiveHost.Json(new[] { language! }) });
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["languages"] = DestructiveHost.Json(new[] { language! }) };

        JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        Assert.Equal(("not_started", 0, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("textsAffected").GetInt32(), dryRun.GetProperty("backup").ValueKind));
        int folders = _host.BackupFolders().Length;

        _host.Approvals.Approve(Tool, _host.Writable, [$"delete language {language} (0 translations)"]);
        JsonElement removed = await _host.CallAsync(Tool, args);

        Assert.Equal(("committed", "AI: eplan_language_remove"), (removed.GetProperty("execution").GetString(), removed.GetProperty("undo").GetString()));
        Assert.Equal(translation.Order(StringComparer.Ordinal), Codes(removed.GetProperty("after"), "translation").Order(StringComparer.Ordinal));
        string backup = removed.GetProperty("backup").GetString()!;
        Assert.True(File.Exists(backup), "No backup copy at " + backup);
        Assert.Equal(folders + 1, _host.BackupFolders().Length);
        List<string> displayAfter = Codes(removed.GetProperty("after"), "display");
        TestContext.Current.TestOutputHelper?.WriteLine($"removed {language} (0 translations): display {string.Join(",", display)} -> {string.Join(",", displayAfter)}");

        // Leave the copy as it was: EPLAN may have changed the displayed languages (A28).
        if (!displayAfter.SequenceEqual(display))
        {
            await _host.CallAsync("eplan_language_set", new() { ["project"] = _host.Writable, ["display"] = DestructiveHost.Json(display.ToArray()) });
        }
    }

    [Fact]
    public async Task Swedish_LosesItsTranslations_AndLeavesTheTranslationLanguages()
    {
        JsonElement before = await SettingsAsync();
        List<string> translation = Codes(before, "translation");
        Assert.SkipWhen(!translation.Contains("sv_SE"), "sv_SE was removed from this copy by an earlier run; copy the sample project again to run this test (README.md).");
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["languages"] = DestructiveHost.Json(_swedish) };
        int texts = (await _host.CallAsync(Tool, new(args) { ["dry_run"] = true })).GetProperty("textsAffected").GetInt32();

        // S23 counted 460 on the sample project. Other write tests change the count: inserted macros add texts, and
        // CheckAndReportTests regenerates reports, which drops the Swedish description of the two topology report
        // pages &EFA4/2 and &EFA4/2.1 and adds one parts list page (459 after it, 2026-10-26).
        Assert.InRange(texts, 450, int.MaxValue);
        _host.Approvals.Approve(Tool, _host.Writable, [$"delete language sv_SE ({texts} translations)"]);
        var clock = Stopwatch.StartNew();
        JsonElement removed = await _host.CallAsync(Tool, args);
        double seconds = clock.Elapsed.TotalSeconds;

        Assert.Equal(("committed", texts), (removed.GetProperty("execution").GetString(), removed.GetProperty("textsAffected").GetInt32()));
        Assert.DoesNotContain("sv_SE", Codes(removed.GetProperty("after"), "translation"));
        Assert.Equal(translation.Count - 1, Codes(removed.GetProperty("after"), "translation").Count);
        string backup = removed.GetProperty("backup").GetString()!;
        Assert.True(File.Exists(backup), "No backup copy at " + backup);
        JsonElement again = await _host.ErrorAsync(Tool, args);
        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (again.GetProperty("reason").GetString(), again.GetProperty("execution").GetString()));

        // Not verified (tool spec 7): whether the backup still has Swedish, and what Ctrl+Z restores; the README says how
        // to check both by hand. The time includes the backup.
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"removed sv_SE and {texts} translations in {seconds:0.0} s; display {string.Join(",", Codes(before, "display"))} -> {string.Join(",", Codes(removed.GetProperty("after"), "display"))}; backup {backup}; hint: {removed.GetProperty("hint").GetString()}");
    }

    [Fact]
    public async Task ALanguageThatIsNoTranslationLanguage_IsInvalid_WithoutAnApprovalOrABackup()
    {
        List<string> translation = Codes(await SettingsAsync(), "translation");
        string missing = _notTranslated.First(code => !translation.Contains(code));
        int questions = _host.Approvals.Questions.Count;
        int folders = _host.BackupFolders().Length;

        JsonElement error = await _host.ErrorAsync(Tool, new() { ["project"] = _host.Writable, ["languages"] = DestructiveHost.Json(new[] { missing }) });

        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(translation, error.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n'));
        Assert.Equal((questions, folders), (_host.Approvals.Questions.Count, _host.BackupFolders().Length));
    }

    /// <summary>The copy's languages now, as a dry run of eplan_language_set reads them: <c>before</c>.</summary>
    private async Task<JsonElement> SettingsAsync() =>
        (await _host.CallAsync("eplan_language_set", new() { ["project"] = _host.Writable, ["display"] = DestructiveHost.Json(_unset), ["dry_run"] = true })).GetProperty("before");

    private static List<string> Codes(JsonElement languages, string list) => [.. languages.GetProperty(list).EnumerateArray().Select(code => code.GetString()!)];
}

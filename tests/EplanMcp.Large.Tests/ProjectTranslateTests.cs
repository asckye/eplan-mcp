using System.Diagnostics;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_project_translate on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_project_translate.md,
/// section 7). S23 r2 translated a German-only "Motor" into the project's languages once the source language was de_DE,
/// and nothing with ##_## (api-notes A28); the translation database has "Motor" with zh_CN "电机". The test translates
/// only a new page of its own, so the copy's texts stay as they were; it sets the source language back and deletes the
/// page at the end. What the spec leaves to this test goes to the test output: whether an existing translation is
/// rewritten from the database, and the time.
/// </summary>
[Trait("Size", "Large")]
public sealed class ProjectTranslateTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_project_translate";
    private const string Unset = "##_##";

    private readonly DestructiveHost _host;

    public ProjectTranslateTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task WithoutASourceLanguage_IsInvalid_WithoutApprovalOrBackup()
    {
        string source = await SourceAsync();
        int folders = _host.BackupFolders().Length;
        try
        {
            await SetSourceAsync(Unset);

            JsonElement error = await _host.ErrorAsync(Tool, new() { ["project"] = _host.Writable });

            Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Contains("eplan_language_set 'source'", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
            Assert.Equal(folders, _host.BackupFolders().Length);
        }
        finally
        {
            await SetSourceAsync(source);
        }
    }

    [Fact]
    public async Task AGermanOnlyPageDescription_IsTranslatedFromTheDatabase()
    {
        string source = await SourceAsync();
        string page = (await _host.NewPagesAsync(1))[0];
        try
        {
            await DescribeAsync(page, "de_DE", "Motor");
            await DescribeAsync(page, "en_US", "Engine");
            await SetSourceAsync("de_DE");
            Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { page }) };

            JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
            Assert.Equal(("not_started", 1, 1, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("textsChecked").GetInt32(), dryRun.GetProperty("textsMissing").GetInt32(), dryRun.GetProperty("backup").ValueKind));
            int into = dryRun.GetProperty("languages").EnumerateArray().Count(code => code.GetString() != "de_DE");
            int folders = _host.BackupFolders().Length;

            _host.Approvals.Approve(Tool, _host.Writable, [$"translate 1 page: 1 of 1 texts miss a translation language; from de_DE into {into} languages"]);
            var clock = Stopwatch.StartNew();
            JsonElement translated = await _host.CallAsync(Tool, args);
            double seconds = clock.Elapsed.TotalSeconds;

            Assert.Equal(("committed", 1, 1, "AI: eplan_project_translate"), (translated.GetProperty("execution").GetString(), translated.GetProperty("textsChecked").GetInt32(), translated.GetProperty("textsChanged").GetInt32(), translated.GetProperty("undo").GetString()));
            JsonElement changed = Assert.Single(translated.GetProperty("changed").EnumerateArray());
            Assert.Equal(("page", page, "PAGE_NOMINATIOMN"), (changed.GetProperty("kind").GetString(), changed.GetProperty("name").GetString(), changed.GetProperty("property").GetString()));
            Assert.DoesNotContain("zh_CN", changed.GetProperty("missingAfter").EnumerateArray().Select(code => code.GetString()));
            string backup = translated.GetProperty("backup").GetString()!;
            Assert.True(File.Exists(backup), "No backup copy at " + backup);
            Assert.Equal(folders + 1, _host.BackupFolders().Length);
            Assert.Equal("电机", await DescriptionAsync(page, "zh_CN"));
            Assert.Equal("Motor", await DescriptionAsync(page, "de_DE"));

            // Not verified by S23 (tool spec 5): whether an existing translation is rewritten from the database.
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{seconds:0.00} s; en_US 'Engine' -> '{await DescriptionAsync(page, "en_US")}'; missingAfter {string.Join(",", changed.GetProperty("missingAfter").EnumerateArray().Select(code => code.GetString()))}; stillMissing {translated.GetProperty("stillMissing").GetInt32()}; backup {backup}");

            await SetSourceAsync(source);
            await _host.DeleteAsync([page]);
        }
        catch
        {
            await SetSourceAsync(source);
            await _host.RemoveQuietlyAsync([page]);
            throw;
        }
    }

    /// <summary>The copy's source language now, as a dry run of eplan_language_set reads it.</summary>
    private async Task<string> SourceAsync() =>
        (await _host.CallAsync("eplan_language_set", new() { ["project"] = _host.Writable, ["display"] = DestructiveHost.Json(new[] { Unset }), ["dry_run"] = true }))
            .GetProperty("before").GetProperty("source").GetString()!;

    private Task<JsonElement> SetSourceAsync(string source) => _host.CallAsync("eplan_language_set", new() { ["project"] = _host.Writable, ["source"] = source });

    private Task<JsonElement> DescribeAsync(string page, string language, string text) => _host.CallAsync("eplan_page_update", new()
    {
        ["project"] = _host.Writable,
        ["language"] = language,
        ["pages"] = DestructiveHost.Json(new[] { new { name = page, description = text } }),
    });

    private async Task<string?> DescriptionAsync(string page, string language) =>
        (await _host.CallAsync("eplan_page_get", new() { ["project"] = _host.Writable, ["page"] = page, ["language"] = language })).GetProperty("page").GetProperty("description").GetString();
}

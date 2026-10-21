using System.Diagnostics;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_language_set on the write copy (README.md), never on the read tests' fixture (docs/tools/eplan_language_set.md,
/// section 7). S23 found the sample project with 20 translation languages and no displayed languages (api-notes A28);
/// the copy keeps what earlier runs did, so the tests start from what a dry run reads. What the spec leaves to these
/// tests goes to the test output: what AddProjectLanguage does, and whether writing the settings with SetStringSetting
/// restores the displayed languages, "##_##" included.
/// </summary>
[Trait("Size", "Large")]
public sealed class LanguageSetTests(LiveEplan eplan)
{
    /// <summary>Languages EPLAN knows (ISOCode.Language) that the sample project is unlikely to have; the first missing one is added.</summary>
    private static readonly string[] _toAdd = ["is_IS", "et_EE", "lv_LV", "lt_LT", "sl_SI"];

    /// <summary>Codes of the form xx_YY that are no translation language of the sample project; the first missing one is used.</summary>
    private static readonly string[] _notTranslated = ["yy_XX", "en_EN", "so_SO"];

    [Fact]
    public async Task Display_EnglishAndChinese_IsReadBack_ThenTheBeforeValuesRestoreIt()
    {
        string project = await eplan.WriteProjectAsync();
        JsonElement before = await Settings(project);
        List<string> translation = Codes(before, "translation");
        List<string> display = Codes(before, "display");
        Assert.Contains("en_US", translation);
        Assert.Contains("zh_CN", translation);
        if (display.SequenceEqual(["en_US", "zh_CN"]))
        {
            // An earlier run stopped halfway: clear them first, so the change below is one.
            await eplan.CallAsync("eplan_language_set", new() { ["project"] = project, ["display"] = Json(["##_##"]) });
            display = ["##_##"];
        }

        var clock = Stopwatch.StartNew();
        JsonElement set = await eplan.CallAsync("eplan_language_set", new() { ["project"] = project, ["display"] = Json(["en_US", "zh_CN"]) });
        double seconds = clock.Elapsed.TotalSeconds;
        JsonElement again = await eplan.CallAsync("eplan_language_set", new() { ["project"] = project, ["display"] = Json(["en_US", "zh_CN"]) });
        JsonElement restored = await eplan.CallAsync("eplan_language_set", new() { ["project"] = project, ["display"] = Json([.. display]) });
        JsonElement after = await Settings(project);

        Assert.Equal(("committed", JsonValueKind.Null), (set.GetProperty("execution").GetString(), set.GetProperty("undo").ValueKind));
        Assert.Equal(["en_US", "zh_CN"], Codes(set.GetProperty("after"), "display"));
        Assert.Equal(translation, Codes(set.GetProperty("after"), "translation"));
        Assert.Equal("not_started", again.GetProperty("execution").GetString());
        Assert.Equal("committed", restored.GetProperty("execution").GetString());
        Assert.Equal(display, Codes(after, "display"));
        Assert.Equal(translation, Codes(after, "translation"));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"display {string.Join(",", display)} -> en_US,zh_CN in {seconds:0.00} s, then back to {string.Join(",", Codes(restored.GetProperty("after"), "display"))}; {translation.Count} translation languages");
    }

    [Fact]
    public async Task Add_ALanguageTheProjectLacks_JoinsTheTranslationLanguages()
    {
        string project = await eplan.WriteProjectAsync();
        JsonElement before = await Settings(project);
        List<string> translation = Codes(before, "translation");
        string? language = _toAdd.FirstOrDefault(code => !translation.Contains(code));
        Assert.SkipWhen(language is null, "Every candidate language is a translation language of the copy already; copy it again (README.md).");

        JsonElement added = await eplan.CallAsync("eplan_language_set", new() { ["project"] = project, ["add"] = Json([language!]) });
        JsonElement again = await eplan.CallAsync("eplan_language_set", new() { ["project"] = project, ["add"] = Json([language!]) });

        Assert.Equal("committed", added.GetProperty("execution").GetString());
        Assert.Equal(translation.Append(language!).Order(StringComparer.Ordinal), Codes(added.GetProperty("after"), "translation").Order(StringComparer.Ordinal));
        Assert.Equal("not_started", again.GetProperty("execution").GetString());

        // Not verified by S23 (tool spec 5): where EPLAN puts the new language, and whether it changes the displayed ones.
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"added {language}: translation {string.Join(",", Codes(added.GetProperty("after"), "translation"))}; display {string.Join(",", Codes(before, "display"))} -> {string.Join(",", Codes(added.GetProperty("after"), "display"))}; hint: {added.GetProperty("hint").GetString()}");
    }

    [Fact]
    public async Task Display_ALanguageThatIsNoTranslationLanguage_IsInvalidWithTheCandidates()
    {
        string project = await eplan.WriteProjectAsync();
        List<string> translation = Codes(await Settings(project), "translation");
        string missing = _notTranslated.First(code => !translation.Contains(code));

        JsonElement error = await eplan.ErrorAsync("eplan_language_set", new() { ["project"] = project, ["display"] = Json([missing]) });

        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(translation, error.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n'));
    }

    /// <summary>The settings now, as a dry run reads them: <c>before</c>.</summary>
    private async Task<JsonElement> Settings(string project) =>
        (await eplan.CallAsync("eplan_language_set", new() { ["project"] = project, ["display"] = Json(["##_##"]), ["dry_run"] = true })).GetProperty("before");

    private static List<string> Codes(JsonElement languages, string list) => [.. languages.GetProperty(list).EnumerateArray().Select(code => code.GetString()!)];

    private static JsonElement Json(string[] codes) => JsonSerializer.SerializeToElement(codes);
}

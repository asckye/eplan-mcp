// Ported in part from TIA Portal Openness MCP, tests/Studio/TiaOpenness.Gui.Tests/StringsTests.cs at commit c89b7c20.
// Copyright (c) 2026 bulaofen0036-coder. MIT License, see THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Text.RegularExpressions;
using EplanMcp.Workbench.Localization;

namespace EplanMcp.Workbench.Tests.Localization;

/// <summary>
/// The catalogue (workbench-ui LOC-*): one table of (key, English, Chinese), so a key cannot miss a language; what can
/// still go wrong is a key declared twice, a blank or untranslated text, placeholders that differ between the two
/// languages (a FormatException when the text is shown), or a key the views use that the table lacks.
/// </summary>
[Trait("Size", "Small")]
public sealed partial class StringsTests
{
    /// <summary>Texts that are the same in both languages on purpose: product names, technical identifiers, formats.</summary>
    private static readonly string[] _sameInBothLanguages = ["App.Name", "Shell.WindowTitle", "Shell.Breadcrumb", "Settings.Chinese", "Settings.English", "Question.SuspendedValue",
        "Clients.Name.ClaudeCode", "Clients.Name.Codex", "Clients.Name.GeminiCli", "Clients.Name.QwenCode", "Clients.Name.KimiCode", "Clients.Name.Codebuddy",
        "Clients.Name.Opencode", "Clients.Name.QwenAgent", "Clients.Name.Cursor", "Clients.Name.VscodeCopilot",
    ];

    public static TheoryData<string, string, string> Entries()
    {
        var data = new TheoryData<string, string, string>();
        foreach ((string key, string en, string zh) in Catalogue.Entries)
        {
            data.Add(key, en, zh);
        }

        return data;
    }

    [Fact]
    public void NoKey_IsDeclaredTwice() =>
        Assert.Empty(Catalogue.Entries.GroupBy(e => e.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));

    [Fact]
    public void BothLanguages_HaveTheSameKeys() =>
        Assert.Equal(Catalogue.English.Keys.Order(StringComparer.Ordinal), Catalogue.Chinese.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(Entries))]
    public void NeitherText_IsBlank(string key, string en, string zh)
    {
        Assert.False(string.IsNullOrWhiteSpace(en), key);
        Assert.False(string.IsNullOrWhiteSpace(zh), key);
    }

    /// <summary>Chinese may reorder the placeholders but must use the same ones (LOC-02).</summary>
    [Theory]
    [MemberData(nameof(Entries))]
    public void BothTexts_UseTheSamePlaceholders_AndFormat(string key, string en, string zh)
    {
        Assert.True(Indexes(en).SequenceEqual(Indexes(zh)), key);
        object[] arguments = [.. Enumerable.Range(0, Indexes(en).DefaultIfEmpty(-1).Max() + 1).Select(i => (object)i)];
        Assert.NotEmpty(string.Format(CultureInfo.InvariantCulture, en, arguments));
        Assert.NotEmpty(string.Format(CultureInfo.InvariantCulture, zh, arguments));
    }

    [Fact]
    public void ChineseTexts_AreTranslated() =>
        Assert.Empty(Catalogue.Entries.Where(e => e.En == e.Zh && !_sameInBothLanguages.Contains(e.Key)).Select(e => e.Key));

    [Fact]
    public void KeysAreDottedIdentifiers() =>
        Assert.All(Catalogue.Entries, e => Assert.Matches(DottedKey(), e.Key));

    [Theory]
    [MemberData(nameof(Entries))]
    public void English_HoldsNoChinese_AndChinese_UsesFullWidthPunctuationWithoutExclamations(string key, string en, string zh)
    {
        // The language picker names each language in its own language.
        Assert.True(key == "Settings.Chinese" || !Cjk().IsMatch(en), key);
        Assert.DoesNotMatch(AsciiPunctuationAfterCjk(), zh);
        Assert.DoesNotContain('！', zh);
        Assert.False(en.Contains("  ", StringComparison.Ordinal) || zh.Contains("  ", StringComparison.Ordinal), key);
    }

    /// <summary>
    /// The Workbench's own views and code, and the theme's scroll bars; the theme's other keys name TIA's pages, which
    /// the Workbench does not use.
    /// </summary>
    [Fact]
    public void EveryKeyTheViewsAndTheCodeUse_IsInTheCatalogue()
    {
        IEnumerable<string> own = SourceScan.Own.SelectMany(f => TrKey().Matches(f.Text).Concat(CodeKey().Matches(f.Text))).Select(m => m.Groups["key"].Value);
        IEnumerable<string> theme = SourceScan.Vendored.SelectMany(f => TrKey().Matches(f.Text).Concat(CodeKey().Matches(f.Text)))
            .Select(m => m.Groups["key"].Value).Where(k => k.StartsWith("Access.Scroll", StringComparison.Ordinal));
        string[] used = [.. own.Concat(theme).Distinct()];

        Assert.Contains("Access.ScrollPosition", used);
        Assert.Contains("Page.Approvals", used);
        Assert.Contains("Result.Approved", used);
        Assert.Contains("Outcome.Unknown", used);
        Assert.All(used, k => Assert.True(Catalogue.English.ContainsKey(k), k));
    }

    /// <summary>
    /// The views hold no text of their own (LOC-01): what a view shows comes from the catalogue, a binding or code. The
    /// breadcrumb's separator is the one exception.
    /// </summary>
    [Fact]
    public void NoView_ShowsALiteralText()
    {
        string[] literals = [.. SourceScan.Own.Where(f => f.Path.EndsWith(".xaml", StringComparison.Ordinal))
            .SelectMany(f => LiteralText().Matches(f.Text).Select(m => $"{f.Path}: {m.Value.Trim()}"))];

        Assert.Equal(["Shell/MainWindow.xaml: Text=\"/\""], literals);
    }

    private static int[] Indexes(string text) => [.. Placeholder().Matches(text).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order()];

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$")]
    private static partial Regex DottedKey();

    [GeneratedRegex(@"[\u3400-\u9fff]")]
    private static partial Regex Cjk();

    [GeneratedRegex(@"[\u3400-\u9fff][,;:?!]")]
    private static partial Regex AsciiPunctuationAfterCjk();

    [GeneratedRegex(@"\s(?:Text|Content|Title|Caption|Header|ToolTip|AutomationProperties\.Name|AutomationProperties\.HelpText)=""[^{""][^""]*""")]
    private static partial Regex LiteralText();

    [GeneratedRegex(@"\{l:Tr (?<key>[A-Za-z0-9.]+)")]
    private static partial Regex TrKey();

    /// <summary>
    /// A catalogue key as a string literal in code: two or more dotted parts, starting with a known area. A literal that
    /// ends with a dot is the start of keys made in code, which their own tests check.
    /// </summary>
    [GeneratedRegex("\"(?<key>(?:App|Access|Shell|Page|Approvals|Settings|Tray|Exit|Pipe|Question|Result|Outcome|Host|Audit|Status|Clients)(?:\\.[A-Za-z0-9]+)+)\"")]
    private static partial Regex CodeKey();
}

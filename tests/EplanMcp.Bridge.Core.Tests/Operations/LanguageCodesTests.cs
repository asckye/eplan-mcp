using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// Language codes in arguments and in the stored settings (docs/tools/eplan_language_set.md, sections 2 and 5): the
/// stored form <c>en_US;zh_CN;</c> and SetDisplayedLanguages' comma-separated argument (S23 r1, api-notes A28).
/// </summary>
[Trait("Size", "Small")]
public sealed class LanguageCodesTests
{
    [Fact]
    public void ForDisplay_SeparatesWithCommasNeverSemicolons()
    {
        Assert.Equal("en_US,zh_CN", LanguageCodes.ForDisplay(["en_US", "zh_CN"]));
        Assert.Equal("en_US", LanguageCodes.ForDisplay(["en_US"]));
    }

    [Theory]
    [InlineData("de_DE;en_US;", new[] { "de_DE", "en_US" })]
    [InlineData("##_##;", new[] { "##_##" })]
    [InlineData("##_##", new[] { "##_##" })]
    [InlineData("", new string[0])]
    public void Split_ReadsTheStoredForm(string stored, string[] codes) => Assert.Equal(codes, LanguageCodes.Split(stored));

    [Fact]
    public void Stored_EndsEveryCodeWithASemicolon() => Assert.Equal("en_US;zh_CN;", LanguageCodes.Stored(["en_US", "zh_CN"]));

    [Fact]
    public void Lists_GivesBothSettingsInStoredOrder()
    {
        var lists = LanguageCodes.Lists(new LanguageSettings { Translation = "zh_CN;de_DE;", Display = "##_##;", Source = "##_##" });

        Assert.Equal(["zh_CN", "de_DE"], lists.Translation);
        Assert.Equal(["##_##"], lists.Display);
    }

    [Fact]
    public void Check_ListsEveryProblemByIndex()
    {
        var violations = new List<string>();

        LanguageCodes.Check(["en_US", "EN_us", null, "en_US", "##_##"], "add", violations);

        Assert.Equal(
            [
                "add[1] is not a language code of the form xx_YY, such as en_US or zh_CN.",
                "add[2] is not a language code of the form xx_YY, such as en_US or zh_CN.",
                "add[3] repeats add[0].",
                "add[4] is not a language code of the form xx_YY, such as en_US or zh_CN.",
            ],
            violations);
    }

    [Fact]
    public void Check_AllowsTheUnsetValueOnlyAloneAndOnlyWhereAsked()
    {
        var violations = new List<string>();

        LanguageCodes.Check(["##_##"], "display", violations, unsetAlone: true);
        LanguageCodes.Check(["##_##", "en_US"], "display", violations, unsetAlone: true);

        Assert.Equal(["display[0] is ##_##, which clears the setting and must be the only entry."], violations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Check_AnEmptyListOrMoreThan10_IsAProblem(int count)
    {
        var violations = new List<string>();

        LanguageCodes.Check([.. Enumerable.Range(0, count).Select(i => "a" + (char)('a' + i) + "_AA")], "add", violations);

        Assert.Equal(["'add' must hold 1 to 10 language codes such as en_US; omit it to leave it out."], violations);
    }

    [Fact]
    public void Check_AnArgumentNotGiven_IsNoProblem()
    {
        var violations = new List<string>();

        LanguageCodes.Check(null, "add", violations);

        Assert.Empty(violations);
    }
}

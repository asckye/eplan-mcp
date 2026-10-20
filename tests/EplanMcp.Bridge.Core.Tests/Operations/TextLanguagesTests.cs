using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class TextLanguagesTests
{
    [Theory]
    [InlineData("de_DE@Leistungsschalter;en_US@Circuit breaker;", "en_US,de_DE")]
    [InlineData("de_DE@Schütz;fr_FR@Contacteur;", "de_DE,fr_FR")]
    [InlineData("zh_CN@;de_DE@Motor;", "zh_CN,de_DE")]
    [InlineData("en_US@Motor;", "en_US")]
    [InlineData("??_??@1,5;", "")]
    [InlineData("", "")]
    [InlineData("Motor", "")]
    public void Fallbacks_ListsTheStoredLanguagesEnglishFirst(string raw, string expected)
    {
        Assert.Equal(expected, string.Join(",", TextLanguages.Fallbacks(raw)));
    }

    [Fact]
    public void Fallbacks_ReadsOnlyCodesThatStartAnEntry()
    {
        // '@' and ';' inside a text: "a@b" is no language, and the ";xx_YY@" in the middle of a text is a harmless extra.
        Assert.Equal(["en_US", "de_DE", "it_IT"], TextLanguages.Fallbacks("de_DE@Kontakt a@b;Teil;it_IT@x;en_US@Contact;"));
    }

    [Fact]
    public void Fallbacks_ListsEachLanguageOnce()
    {
        Assert.Equal(["en_US", "de_DE"], TextLanguages.Fallbacks("de_DE@a;en_US@b;de_DE@c;en_US@d;"));
    }
}

using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>Changing one language of a page description (docs/tools/eplan_page_update.md, sections 5 and 7).</summary>
[Trait("Size", "Small")]
public sealed class PageDescriptionEditTests
{
    private static readonly TextEntry[] _three = [new("de_DE", "Netz"), new("zh_CN", "电源"), new("en_US", "Power")];

    [Fact]
    public void With_ReplacesTheLanguageWhereItStands()
    {
        Assert.Equal(["de_DE@Netz", "zh_CN@主回路", "en_US@Power"], Lines(PageDescriptionEdit.With(_three, "zh_CN", "主回路")));
    }

    [Fact]
    public void With_AddsAMissingLanguageAtTheEnd()
    {
        Assert.Equal(["de_DE@Netz", "zh_CN@电源", "en_US@Power", "fr_FR@Réseau"], Lines(PageDescriptionEdit.With(_three, "fr_FR", "Réseau")));
    }

    [Fact]
    public void With_ClearsOnlyThatLanguage()
    {
        Assert.Equal(["de_DE@Netz", "en_US@Power"], Lines(PageDescriptionEdit.With(_three, "ZH_cn", null)));
        Assert.Empty(PageDescriptionEdit.With([new TextEntry("zh_CN", "电源")], "zh_CN", null));
    }

    [Fact]
    public void With_KeepsTheLanguageIndependentEntry()
    {
        Assert.Equal(["??_??@1,5", "zh_CN@电源"], Lines(PageDescriptionEdit.With([new TextEntry("??_??", "1,5")], "zh_CN", "电源")));
    }

    [Fact]
    public void Difference_IsNullWhenOnlyTheLanguageChangedAsAsked()
    {
        PageDescription before = Description(_three);
        PageDescription after = Description(PageDescriptionEdit.With(_three, "zh_CN", "主回路"));

        Assert.Null(PageDescriptionEdit.Difference(before, after, "zh_CN", "主回路"));
        Assert.Null(PageDescriptionEdit.Difference(before, Description([_three[0], _three[2]]), "zh_CN", null));
    }

    [Fact]
    public void Difference_NamesTheLanguageEplanDidNotKeep()
    {
        PageDescription before = Description(_three);

        Assert.Equal("zh_CN", PageDescriptionEdit.Difference(before, Description([_three[0], new TextEntry("zh_CN", "主回路 "), _three[2]]), "zh_CN", "主回路"));
        Assert.Equal("en_US", PageDescriptionEdit.Difference(before, Description([_three[0], new TextEntry("zh_CN", "主回路")]), "zh_CN", "主回路"));
        Assert.Equal("it_IT", PageDescriptionEdit.Difference(before, Description([.. _three, new TextEntry("it_IT", "x")]), "zh_CN", "电源"));
    }

    [Fact]
    public void Difference_TreatsAnEmptyTextAsNone()
    {
        PageDescription before = Description([new TextEntry("de_DE", string.Empty), new TextEntry("zh_CN", "电源")]);

        Assert.Null(PageDescriptionEdit.Difference(before, Description([new TextEntry("zh_CN", "主回路")]), "zh_CN", "主回路"));
    }

    [Theory]
    [InlineData("de_DE@Netz;zh_CN@电源;", "de_DE,zh_CN")]
    [InlineData("??_??@1,5;", "??_??")]
    [InlineData("zh_CN@a;??_??@b;zh_CN@c;", "zh_CN,??_??")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Stored_ListsEveryLanguageWithTheIndependentOne(string? raw, string expected)
    {
        Assert.Equal(expected, string.Join(",", TextLanguages.Stored(raw)));
    }

    private static PageDescription Description(IEnumerable<TextEntry> entries)
    {
        TextEntry[] all = [.. entries];
        return new PageDescription(string.Concat(all.Select(e => $"{e.Language}@{e.Text};")), all);
    }

    private static string[] Lines(IEnumerable<TextEntry> entries) => [.. entries.Select(e => $"{e.Language}@{e.Text}")];
}

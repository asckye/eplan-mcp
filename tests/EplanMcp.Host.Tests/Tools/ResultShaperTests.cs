using System.Globalization;
using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ResultShaperTests
{
    private static readonly string _listing = PageCursor.Fingerprint("eplan_page_list", "Line1");

    [Fact]
    public void CleanText_RemovesControlCharactersButKeepsNewlineAndTab()
    {
        Assert.Equal("ab\ncd\tefgh", ResultShaper.CleanText("a\0b\r\nc\u001Bd\te\u007Ff\u0085g\u009Fh"));
        Assert.Equal("电气 -K1 Schütz", ResultShaper.CleanText("电气 -K1 Schütz"));
    }

    [Fact]
    public void CleanText_RemovesBidirectionalControlsButKeepsOtherFormatCharacters()
    {
        // "Trojan Source": an override makes "-K1 delete all" display as something else to the engineer.
        Assert.Equal("-K1 lla eteled", ResultShaper.CleanText("-K1 ‮lla eteled‬"));
        Assert.Equal("ab", ResultShaper.CleanText("⁦a⁩‪b‬"));
        Assert.Equal("👨‍🔧", ResultShaper.CleanText("👨‍🔧"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CleanText_NullAndEmpty_StayAsTheyAre(string? text)
    {
        Assert.Equal(text, ResultShaper.CleanText(text));
    }

    [Fact]
    public void CleanText_TextAtTheLimit_IsReturnedWhole()
    {
        string text = new('x', ResultShaper.MaxTextLength);

        Assert.Equal(text, ResultShaper.CleanText(text));
    }

    [Fact]
    public void CleanText_LongerText_IsCutTo500CharactersAndShowsItsLength()
    {
        Assert.Equal(new string('x', 500) + " … [truncated, 1234 characters in total]", ResultShaper.CleanText(new string('x', 1234)));
    }

    [Fact]
    public void CleanText_MeasuresTheTextAfterRemovingControlCharacters()
    {
        Assert.Equal("abc", ResultShaper.CleanText(new string('\0', 600) + "abc"));
        Assert.EndsWith("[truncated, 501 characters in total]", ResultShaper.CleanText(new string('y', 501) + "\r"), StringComparison.Ordinal);
    }

    [Fact]
    public void CleanText_DoesNotCutASurrogatePairInHalf()
    {
        string text = new string('x', 499) + "\U0001F600" + new string('x', 100);

        Assert.Equal(new string('x', 499) + " … [truncated, 601 characters in total]", ResultShaper.CleanText(text));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abcdefgh", 2)]
    [InlineData("abcde", 2)]
    [InlineData("电气图纸", 4)]
    [InlineData("-K1 电机", 3)]
    public void EstimateTokens_CountsFourAsciiCharactersOrOneOtherCharacterPerToken(string json, int tokens)
    {
        Assert.Equal(tokens, ResultShaper.EstimateTokens(json));
    }

    [Fact]
    public void FitPage_PageWithinTheBudget_IsLeftAsItIs()
    {
        TestPage page = Page(count: 10, length: 10);
        page.NextCursor = "next";

        ResultShaper.FitPage(page, 0, _listing);

        Assert.Equal(10, page.Entries.Count);
        Assert.False(page.Truncated);
        Assert.Null(page.Hint);
        Assert.Equal("next", page.NextCursor);
    }

    [Fact]
    public void FitPage_PageOverTheBudget_DropsEntriesFromTheEndAndPointsTheCursorAtTheFirstDropped()
    {
        // About 100 tokens per entry: 10,000 against the 5,000 default.
        TestPage page = Page(count: 100, length: 180); // about 9k tokens with the JSON counted twice: over 5k, under 20k
        List<string> original = [.. page.Entries];

        ResultShaper.FitPage(page, offset: 40, _listing);

        int kept = page.Entries.Count;
        Assert.InRange(kept, 1, 99);
        Assert.Equal(original.Take(kept), page.Entries);
        Assert.True(page.Truncated);
        Assert.Equal(40 + kept, PageCursor.Decode(page.NextCursor!, _listing));
        Assert.Contains("'nextCursor' as 'cursor'", page.Hint, StringComparison.Ordinal);
        Assert.InRange(Estimate(page), 1, ResultShaper.DefaultTokenBudget);

        // As many as fit: one more entry would not.
        page.Entries.Add(original[kept]);
        Assert.True(Estimate(page) > ResultShaper.DefaultTokenBudget);
    }

    [Fact]
    public void FitPage_DetailedBudget_FitsWhatTheDefaultCuts()
    {
        TestPage page = Page(count: 100, length: 180); // about 9k tokens with the JSON counted twice: over 5k, under 20k

        ResultShaper.FitPage(page, 0, _listing, ResultShaper.MaxTokenBudget);

        Assert.Equal(100, page.Entries.Count);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void FitPage_NotEvenOneEntryFits_ReturnsNoEntriesAndKeepsTheCursorOnTheFirst()
    {
        TestPage page = Page(count: 3, length: 4 * ResultShaper.DefaultTokenBudget);

        ResultShaper.FitPage(page, offset: 7, _listing);

        Assert.Empty(page.Entries);
        Assert.True(page.Truncated);
        Assert.Equal(7, PageCursor.Decode(page.NextCursor!, _listing));
        Assert.Contains("alone exceeds", page.Hint, StringComparison.Ordinal);
        Assert.InRange(Estimate(page), 1, ResultShaper.DefaultTokenBudget);
    }

    [Fact]
    public void FitPage_BudgetAboveTheHardCap_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResultShaper.FitPage(Page(1, 1), 0, _listing, ResultShaper.MaxTokenBudget + 1));
    }

    [Fact]
    public void FitItems_ResultWithinTheBudget_IsLeftAsItIs()
    {
        TestPage page = Page(count: 10, length: 10);

        Assert.True(ResultShaper.Fits(page, ResultShaper.DefaultTokenBudget));
        Assert.Equal(0, ResultShaper.FitItems(page, page.Entries));
        Assert.Equal(10, page.Entries.Count);
    }

    [Fact]
    public void FitItems_ResultOverTheBudget_KeepsTheMostEntriesFromTheStartThatFit()
    {
        TestPage page = Page(count: 40, length: 1000);
        string[] all = [.. page.Entries];
        Assert.False(ResultShaper.Fits(page, ResultShaper.DefaultTokenBudget));

        int dropped = ResultShaper.FitItems(page, page.Entries);

        Assert.InRange(page.Entries.Count, 1, 39);
        Assert.Equal(40 - page.Entries.Count, dropped);
        Assert.Equal(all.Take(page.Entries.Count), page.Entries);
        Assert.InRange(Estimate(page), 1, ResultShaper.DefaultTokenBudget);
        page.Entries.Add(all[page.Entries.Count]);
        Assert.False(ResultShaper.Fits(page, ResultShaper.DefaultTokenBudget), "one more entry would not have fitted");
    }

    [Fact]
    public void FitItems_OtherPartsAloneOverTheBudget_EmptiesTheList()
    {
        TestPage page = Page(count: 3, length: 10);
        page.Hint = new string('h', 4 * ResultShaper.DefaultTokenBudget);

        Assert.Equal(3, ResultShaper.FitItems(page, page.Entries));
        Assert.Empty(page.Entries);
    }

    [Fact]
    public void FitItems_NullOrEmptyList_DropsNothing()
    {
        TestPage page = Page(count: 3, length: 4 * ResultShaper.DefaultTokenBudget);

        Assert.Equal(0, ResultShaper.FitItems<string>(page, null));
        Assert.Equal(0, ResultShaper.FitItems(page, new List<string>()));
        Assert.Equal(3, page.Entries.Count);
    }

    [Fact]
    public void FitItems_BudgetAboveTheHardCap_IsRejected()
    {
        TestPage page = Page(count: 1, length: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => ResultShaper.FitItems(page, page.Entries, ResultShaper.MaxTokenBudget + 1));
    }

    private static TestPage Page(int count, int length) =>
        new() { Entries = [.. Enumerable.Range(0, count).Select(i => i.ToString("D3", CultureInfo.InvariantCulture).PadRight(length, 'x'))] };

    private static int Estimate(TestPage page) => ResultShaper.ResultTokens(JsonSerializer.Serialize(page, HostJson.Options));

    private sealed class TestPage : IPagedResult<string>
    {
        public List<string> Entries { get; set; } = [];

        public string? NextCursor { get; set; }

        public bool Truncated { get; set; }

        public string? Hint { get; set; }

        List<string> IPagedResult<string>.Items => Entries;
    }
}

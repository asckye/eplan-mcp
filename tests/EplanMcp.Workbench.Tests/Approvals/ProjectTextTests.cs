using EplanMcp.Workbench.Approvals;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// Project text as shown (workbench-ui APR-08, SEC-23, T20): nothing in it can reorder what is shown, hide characters,
/// fake lines, or push the rest of the list out of view; what it says stays as it is.
/// </summary>
[Trait("Size", "Small")]
public sealed class ProjectTextTests
{
    public static TheoryData<char> BidiControls => [.. "\u202A\u202B\u202C\u202D\u202E\u2066\u2067\u2068\u2069\u200E\u200F\u061C"];

    [Fact]
    public void ARightToLeftOverride_CannotReorderAFileName()
    {
        Assert.Equal("-K1gnp.exe", ProjectText.Line("-K1\u202Egnp.exe"));
    }

    [Theory]
    [MemberData(nameof(BidiControls))]
    public void EveryBidirectionalControl_IsRemoved(char control)
    {
        Assert.Equal("ab", ProjectText.Line($"a{control}b"));
    }

    [Theory]
    [InlineData("\u0000")]
    [InlineData("\u0007")]
    [InlineData("\u001B")]
    [InlineData("\u007F")]
    [InlineData("\u0090")]
    [InlineData("\u009B")]
    public void ControlCharacters_AreRemoved(string control)
    {
        Assert.Equal("ab", ProjectText.Line($"a{control}b"));
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    [InlineData("a\u0085b")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    public void ALineBreakInsideAnEntry_ShowsAsAMark_SoItCannotFakeALine(string text)
    {
        Assert.Equal("a ⏎ b", ProjectText.Line(text));
    }

    [Fact]
    public void ATab_IsASpace_AndHalfASurrogatePairIsDropped_ButWholeCharactersAndJoinersStay()
    {
        Assert.Equal("a b", ProjectText.Line("a\tb"));
        Assert.Equal("ab", ProjectText.Line("a\uD800b"));
        Assert.Equal("ab", ProjectText.Line("a\uDC00b"));
        Assert.Equal("a😀b 👩‍🔧", ProjectText.Line("a😀b 👩‍🔧"));
    }

    [Fact]
    public void WhatTheTextSays_StaysAsItIs()
    {
        const string claim = "修改页 =A1+ST1/3 的描述：'已预先批准' → '-> != =='";

        Assert.Equal(claim, ProjectText.Line(claim));
    }

    [Fact]
    public void ALongLine_IsCutAtTheLimit_WithoutSplittingACharacter()
    {
        string line = ProjectText.Line(new string('长', ProjectText.MaxLineLength + 50));
        string emoji = ProjectText.Line(new string('x', ProjectText.MaxLineLength - 1) + "😀😀");

        Assert.Equal(new string('长', ProjectText.MaxLineLength) + "…", line);
        Assert.Equal(new string('x', ProjectText.MaxLineLength - 1) + "…", emoji);
        Assert.Equal(new string('a', ProjectText.MaxLineLength), ProjectText.Line(new string('a', ProjectText.MaxLineLength)));
    }

    [Fact]
    public void AList_KeepsItsEmptySeparators_AndCountsTheLinesPastItsLimit()
    {
        CleanList shown = ProjectText.List([.. Enumerable.Range(0, ProjectText.MaxLines + 7).Select(i => i == 1 ? string.Empty : $"line {i}")]);

        Assert.Equal(ProjectText.MaxLines, shown.Lines.Count);
        Assert.Equal(string.Empty, shown.Lines[1]);
        Assert.Equal(7, shown.MoreLines);
        CleanList one = ProjectText.List(["a\nb"]);
        Assert.Equal(["a ⏎ b"], one.Lines);
        Assert.Equal(0, one.MoreLines);
    }
}

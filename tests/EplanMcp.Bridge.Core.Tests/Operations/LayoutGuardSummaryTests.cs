using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardSummaryTests
{
    [Fact]
    public void Summary_KeepsTwentyLinesAndCountsTheRest()
    {
        string[] messages = [.. Enumerable.Range(1, 25).Select(i => FormattableString.Invariant($"Text {i:00} touches a line."))];

        string[] lines = LayoutGuard.Summary(messages).Split('\n');

        Assert.Equal(LayoutGuard.SummaryLines + 1, lines.Length);
        Assert.Equal(messages.Take(LayoutGuard.SummaryLines), lines.Take(LayoutGuard.SummaryLines));
        Assert.Equal("Layout: 5 more warnings not shown; run eplan_page_review on the page for the full list.", lines[^1]);
    }

    [Fact]
    public void Summary_UpToTwentyLines_IsUnchanged()
    {
        string[] messages = ["Layout check passed.", "Text A touches a line.\nText B touches a line.", null!, "", "Text A touches a line."];

        Assert.Equal("Layout check passed.\nText A touches a line.\nText B touches a line.", LayoutGuard.Summary(messages));
        Assert.Equal("", LayoutGuard.Summary([]));
    }
}

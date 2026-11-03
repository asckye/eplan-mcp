using System.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardSearchBudgetTests
{
    [Fact]
    public void DefaultText_ZeroBudgetStopsSearch_WithoutChangingUnlimitedSearch()
    {
        LayoutGuard.Model model = LayoutGuardDefaultTextTests.Model();
        DrawingPlacement text = LayoutGuardDefaultTextTests.CableText(model);

        LayoutGuard.Violation unlimited = Assert.Single(LayoutGuard.Check(model, [text]));
        LayoutGuard.Violation timedOut = Assert.Single(LayoutGuard.Check(model, [text], searchBudget: TimeSpan.Zero));

        Assert.NotNull(unlimited.NearestFreePosition);
        Assert.Null(timedOut.NearestFreePosition);
        Assert.Contains("time limit", timedOut.Message, StringComparison.Ordinal);
        Assert.Contains("eplan_page_review", timedOut.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("No free position found", timedOut.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindFree_ExpiredBeforeFirstCandidate_ReturnsNullImmediately()
    {
        LayoutGuard.Model model = LayoutGuardDefaultTextTests.Model();
        DrawingPlacement text = LayoutGuardDefaultTextTests.CableText(model);
        int checks = 0;

        double[]? free = LayoutGuard.FindFree(model, text, [text], expired: () => { checks++; return true; });

        Assert.Null(free);
        Assert.Equal(1, checks);
    }
}

[Trait("Size", "Medium")]
public sealed class LayoutGuardAfterWritingSearchBudgetTests
{
    [Fact]
    public void AfterWriting_DenseDefaultTexts_SharesSearchBudgetAndReturnsWithinFifteenSeconds()
    {
        LayoutGuard.Model before = LayoutGuardDefaultTextTests.Model();
        LayoutGuard.Model actual = LayoutGuardDefaultTextTests.Model();
        before.Lines.Clear();
        actual.Lines.Clear();
        for (int i = 0; i < 75; i++)
        {
            LayoutGuard.Model pair = LayoutGuardDefaultTextTests.Model();
            DrawingPlacement text = LayoutGuardDefaultTextTests.CableText(pair);
            DrawingPlacement cable = pair.Page.Placements[0];
            cable.Locator.Oid = text.Locator.Oid = i + 1;
            double[] delta = [-192 + i % 15 * 4, i / 15 * 4];
            actual.Page.Placements.Add(LayoutGuard.Shift(cable, delta));
            actual.Page.Placements.Add(LayoutGuard.Shift(text, delta));
        }
        Assert.Equal(150, actual.Page.Placements.Count);

        var timer = Stopwatch.StartNew();
        List<string> checks = LayoutGuard.AfterWriting(before, actual);
        timer.Stop();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"AfterWriting took {timer.Elapsed}.");
        Assert.Contains(checks, message => message.Contains("time limit", StringComparison.Ordinal));
    }
}

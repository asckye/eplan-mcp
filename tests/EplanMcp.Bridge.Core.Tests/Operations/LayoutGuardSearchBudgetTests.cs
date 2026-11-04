using System.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardSearchBudgetTests
{
    [Fact]
    public void DefaultText_ZeroBudgetStopsSearch_WithoutChangingDefaultSearch()
    {
        LayoutGuard.Model model = LayoutGuardDefaultTextTests.Model();
        DrawingPlacement text = LayoutGuardDefaultTextTests.CableText(model);

        LayoutGuard.Violation withDefaultBudget = Assert.Single(LayoutGuard.Check(model, [text]));
        LayoutGuard.Violation timedOut = Assert.Single(LayoutGuard.Check(model, [text], searchBudget: TimeSpan.Zero));

        Assert.NotNull(withDefaultBudget.NearestFreePosition);
        Assert.Null(timedOut.NearestFreePosition);
        Assert.Contains("time limit", timedOut.Message, StringComparison.Ordinal);
        Assert.Contains("eplan_page_review", timedOut.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("No free position found", timedOut.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Conflicts_ZeroBudgetKeepsRulesObjectsGeometryAndRefusal()
    {
        LayoutGuard.Model model = LayoutGuardTests.Model();
        model.Page.Placements.Add(LayoutGuardTests.Row("Function", 1));
        DrawingPlacement[] proposed = [LayoutGuardTests.Row("Function", 2), LayoutGuardTests.Row("Function", 3)];

        List<LayoutGuard.Violation> withoutSuggestions = LayoutGuard.Check(model, proposed, suggest: false);
        List<LayoutGuard.Violation> expired = LayoutGuard.Check(model, proposed, searchBudget: TimeSpan.Zero);

        Assert.NotEmpty(expired);
        Assert.Equal(withoutSuggestions.Count, expired.Count);
        for (int i = 0; i < expired.Count; i++)
        {
            Assert.Equal(withoutSuggestions[i].Rule, expired[i].Rule);
            Assert.Equal(withoutSuggestions[i].Objects, expired[i].Objects);
            Assert.Equal(withoutSuggestions[i].Geometry, expired[i].Geometry);
            Assert.Equal(withoutSuggestions[i].Warning, expired[i].Warning);
            Assert.False(expired[i].Warning);
            Assert.Null(expired[i].NearestFreePosition);
            Assert.StartsWith(withoutSuggestions[i].Message, expired[i].Message, StringComparison.Ordinal);
            Assert.Contains("time limit", expired[i].Message, StringComparison.Ordinal);
        }
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

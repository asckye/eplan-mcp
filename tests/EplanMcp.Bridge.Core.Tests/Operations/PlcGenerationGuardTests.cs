using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlcGenerationGuardTests
{
    private const string Rack = "=K1+UC1&EFR1/1";
    private const string Overview = "=K1+UC1&EFA1/1";

    [Fact]
    public void UnchangedPlacementsAndOnlyRequestedNewPages_HaveNoProblems()
    {
        var functions = new Dictionary<long, string?> { [1] = Rack };

        Assert.Empty(PlcGenerationGuard.Problems(functions, functions, [Rack], [Rack, Overview], [Overview]));
    }

    [Fact]
    public void PreviouslyPlacedFunctionNowUnplaced_IsAProblem()
    {
        var before = new Dictionary<long, string?> { [1] = Rack };
        var after = new Dictionary<long, string?> { [1] = null };

        string problem = Assert.Single(PlcGenerationGuard.Problems(before, after, [Rack], [Rack, Overview], [Overview]));

        Assert.Contains(Rack, problem, StringComparison.Ordinal);
        Assert.Contains("no longer placed", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviouslyPlacedFunctionMissing_IsAProblem()
    {
        var before = new Dictionary<long, string?> { [1] = Rack };

        Assert.Single(PlcGenerationGuard.Problems(before, new Dictionary<long, string?>(), [Rack], [Rack], [Overview]));
    }

    [Fact]
    public void PreviouslyPlacedFunctionMovedToRequestedPage_IsAProblem()
    {
        var before = new Dictionary<long, string?> { [1] = Rack };
        var after = new Dictionary<long, string?> { [1] = Overview };

        string problem = Assert.Single(PlcGenerationGuard.Problems(before, after, [Rack], [Rack, Overview], [Overview]));

        Assert.Contains(Rack, problem, StringComparison.Ordinal);
        Assert.Contains(Overview, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewGenerationLosesEightRackPlacementsAndCreatesSixMultilinePages_ReportsBoth()
    {
        Dictionary<long, string?> before = Enumerable.Range(1, 8).ToDictionary(id => (long)id, _ => (string?)Rack);
        Dictionary<long, string?> after = before.Keys.ToDictionary(id => id, _ => (string?)null);
        string[] extraPages = [.. Enumerable.Range(1, 6).Select(number => "=K1+UC1&EFS1/" + number)];

        IReadOnlyList<string> problems = PlcGenerationGuard.Problems(
            before, after, [Rack], [Rack, Overview, .. extraPages], [Overview]);

        Assert.Equal(14, problems.Count);
        Assert.All(extraPages, page => Assert.Contains(problems, problem => problem.Contains(page, StringComparison.Ordinal)));
    }

    [Fact]
    public void ExtraNewPage_IsAProblem()
    {
        var functions = new Dictionary<long, string?> { [1] = Rack };
        const string extraPage = "=K1+UC1&EFS1/1";

        string problem = Assert.Single(PlcGenerationGuard.Problems(
            functions, functions, [Rack], [Rack, Overview, extraPage], [Overview]));

        Assert.Contains(extraPage, problem, StringComparison.Ordinal);
    }
}

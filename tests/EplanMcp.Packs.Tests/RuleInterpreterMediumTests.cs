using static EplanMcp.Packs.Tests.RuleInterpreterFixture;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class RuleInterpreterMediumTests
{
    [Fact]
    public async Task Cancellation_BeforeEvaluation_IsHonoured()
    {
        var fixture = new RuleInterpreterFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuleInterpreter().EvaluateAsync(
            [fixture.Rule("{\"literal\":true}")], fixture, fixture.Config, fixture.Parameters, cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_DuringEvaluation_IsHonouredBeforeReturningPass()
    {
        var fixture = new RuleInterpreterFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Properties[("-M1", "function.main")] = Data("true", "boolean");
        // Cancelling inside the read needs no second thread to be scheduled in time; the read still answers true, so only the token can stop a Pass.
        fixture.OnProperty = _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuleInterpreter(new ManualRuleClock()).EvaluateAsync(
            [fixture.Rule("{\"property\":\"function.main\"}")], fixture, fixture.Config, fixture.Parameters, cancellation.Token));
    }

    [Fact]
    public async Task Evaluation_500Rules_ReadsEachFactOncePerRule()
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Kinds["function"] = [.. Enumerable.Range(1, 20).Select(i => "-M" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        foreach (string locator in fixture.Kinds["function"])
        {
            fixture.Properties[(locator, "function.main")] = Data("true", "boolean");
        }

        RuleDefinition rule = fixture.Rule("{\"property\":\"function.main\"}");
        RuleDefinition[] rules = [.. Enumerable.Range(1, 500).Select(i => rule with { Rule = "rule/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) })];
        // A clock that stands still: the budgets are the interpreter's own and have tests of their own, so a busy machine must not reach them here.
        IReadOnlyList<RuleResult> results = await new RuleInterpreter(new ManualRuleClock()).EvaluateAsync(rules, fixture, fixture.Config, fixture.Parameters, TestContext.Current.CancellationToken);

        Assert.Equal(10_000, results.Count);
        Assert.All(results, r => Assert.Equal(RuleStatus.Pass, r.Status));
        Assert.Equal(500, results.Select(r => r.Rule).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(10_000, fixture.Reads); // what the time depends on: no rule makes the others read again
    }
}

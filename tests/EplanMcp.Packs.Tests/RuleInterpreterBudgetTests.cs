using static EplanMcp.Packs.Tests.RuleInterpreterFixture;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class RuleInterpreterBudgetTests
{
    [Theory]
    [InlineData(2, RuleStatus.Pass)]
    [InlineData(1, RuleStatus.Unknown)]
    public async Task Visits_ExactBoundary_EnforcesDeterministicLimit(int visits, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        var interpreter = new RuleInterpreter(new ManualRuleClock()) { VisitLimit = visits };

        Assert.Equal(expected, Assert.Single(await fixture.Evaluate(fixture.Rule("{\"literal\":true}"), interpreter)).Status);
        Assert.Equal(100_000, RuleInterpreter.MaxVisits);
    }

    [Fact]
    public async Task Visits_AcrossObjects_InvalidatesEntireRule()
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Kinds["function"] = ["-M1", "-M2", "-M3"];
        var interpreter = new RuleInterpreter(new ManualRuleClock()) { VisitLimit = 3 };
        IReadOnlyList<RuleResult> results = await fixture.Evaluate(fixture.Rule("{\"literal\":true}"), interpreter);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(RuleStatus.Unknown, r.Status));
        Assert.All(results, r => Assert.Contains(r.Evidence, e => !e.Complete && e.Note == "Rule node visit limit exceeded."));
    }

    [Fact]
    public async Task Visits_SetsAndRelations_CountEntriesAndTargetPredicates()
    {
        var fixture = new RuleInterpreterFixture();
        var interpreter = new RuleInterpreter(new ManualRuleClock()) { VisitLimit = 4 };
        RuleResult set = Assert.Single(await fixture.Evaluate(fixture.Rule("""{"in":[{"literal":1},{"literal":[1,2]}]}"""), interpreter));
        fixture.Relations[("-M1", "function.connections")] = new(["c1", "c2", "c3"], true);
        RuleResult relation = Assert.Single(await fixture.Evaluate(fixture.Rule("""{"every":{"relation":"function.connections","where":{"literal":true}}}"""), interpreter));

        Assert.Equal(RuleStatus.Unknown, set.Status);
        Assert.Equal(RuleStatus.Unknown, relation.Status);
    }

    [Theory]
    [InlineData(999, RuleStatus.Pass)]
    [InlineData(1000, RuleStatus.Unknown)]
    public async Task Clock_TimeSpentInLastLookup_IsCheckedBeforeReturning(int milliseconds, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        var clock = new ManualRuleClock();
        fixture.Properties[("-M1", "function.main")] = Data("true", "boolean");
        fixture.OnProperty = _ => clock.Ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule("{\"property\":\"function.main\"}"), new(clock)));

        Assert.Equal(expected, result.Status);
        if (expected == RuleStatus.Unknown)
        {
            Assert.Contains(result.Evidence, e => e.Note == "Rule time limit exceeded.");
        }
    }

    [Fact]
    public async Task Clock_RuleTimeAcrossObjects_InvalidatesEarlierPass()
    {
        var fixture = new RuleInterpreterFixture();
        var clock = new ManualRuleClock();
        fixture.Kinds["function"] = ["-M1", "-M2"];
        fixture.Properties[("-M1", "function.main")] = Data("true", "boolean");
        fixture.Properties[("-M2", "function.main")] = Data("true", "boolean");
        fixture.OnProperty = owner => clock.Ticks += owner == "-M2" ? TimeSpan.TicksPerSecond : 0;

        Assert.All(await fixture.Evaluate(fixture.Rule("{\"property\":\"function.main\"}"), new(clock)), r => Assert.Equal(RuleStatus.Unknown, r.Status));
    }

    [Fact]
    public async Task Clock_EvaluationTimeAcrossRules_InvalidatesEarlierPassAndSkipsLaterWork()
    {
        var fixture = new RuleInterpreterFixture();
        var clock = new ManualRuleClock();
        fixture.Properties[("-M1", "function.main")] = Data("true", "boolean");
        fixture.OnProperty = _ => clock.Ticks += fixture.Reads == 2 ? TimeSpan.FromSeconds(30).Ticks : 0;
        RuleDefinition rule = fixture.Rule("{\"property\":\"function.main\"}");
        IReadOnlyList<RuleResult> results = await new RuleInterpreter(clock).EvaluateAsync([rule, rule, rule], fixture, fixture.Config, fixture.Parameters, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Reads);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(RuleStatus.Unknown, r.Status));
        Assert.All(results, r => Assert.Contains(r.Evidence, e => e.Note == "Evaluation time limit exceeded."));
    }

    [Fact]
    public async Task Limits_ResetForNextRule_AndNextEvaluation()
    {
        var fixture = new RuleInterpreterFixture();
        var clock = new ManualRuleClock();
        fixture.Properties[("-M1", "function.main")] = Data("true", "boolean");
        fixture.OnProperty = _ => clock.Ticks += TimeSpan.TicksPerSecond;
        RuleDefinition slow = fixture.Rule("{\"property\":\"function.main\"}");
        RuleDefinition fast = fixture.Rule("{\"literal\":true}");
        var interpreter = new RuleInterpreter(clock) { VisitLimit = 2 };
        IReadOnlyList<RuleResult> results = await interpreter.EvaluateAsync([slow, fast], fixture, fixture.Config, fixture.Parameters, TestContext.Current.CancellationToken);

        Assert.Equal(RuleStatus.Unknown, results[0].Status);
        Assert.Equal(RuleStatus.Pass, results[1].Status);
        Assert.Equal(RuleStatus.Pass, Assert.Single(await fixture.Evaluate(fast, interpreter)).Status);
    }

    [Fact]
    public async Task Cabinet_Over500Rules_ReturnsUnknownWithoutEvaluatingPredicates()
    {
        var fixture = new RuleInterpreterFixture();
        RuleDefinition rule = fixture.Rule("{\"property\":\"function.main\"}");
        IReadOnlyList<RuleResult> results = await new RuleInterpreter(new ManualRuleClock()).EvaluateAsync(
            Enumerable.Repeat(rule, 501).ToArray(), fixture, fixture.Config, fixture.Parameters, TestContext.Current.CancellationToken);

        Assert.Equal(501, results.Count);
        Assert.Equal(0, fixture.Reads);
        Assert.All(results, r => Assert.Equal(RuleStatus.Unknown, r.Status));
        Assert.All(results, r => Assert.Contains(r.Evidence, e => e.Note == "Cabinet rule limit exceeded."));
    }
}

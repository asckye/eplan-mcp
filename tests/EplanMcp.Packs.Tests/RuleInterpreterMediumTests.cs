using System.Diagnostics;
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
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Properties[("-M1", "function.main")] = Data("true", "boolean");
        fixture.OnProperty = _ =>
        {
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(2)));
        };
        Task<IReadOnlyList<RuleResult>> evaluation = new RuleInterpreter().EvaluateAsync(
            [fixture.Rule("{\"property\":\"function.main\"}")], fixture, fixture.Config, fixture.Parameters, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
        }
        finally
        {
            release.Set();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => evaluation);
    }

    [Fact]
    public async Task Evaluation_500Rules_CompletesWithinBudget()
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Kinds["function"] = [.. Enumerable.Range(1, 20).Select(i => "-M" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        foreach (string locator in fixture.Kinds["function"])
        {
            fixture.Properties[(locator, "function.main")] = Data("true", "boolean");
        }

        RuleDefinition rule = fixture.Rule("{\"property\":\"function.main\"}");
        RuleDefinition[] rules = [.. Enumerable.Range(1, 500).Select(i => rule with { Rule = "rule/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) })];
        var timer = Stopwatch.StartNew();
        IReadOnlyList<RuleResult> results = await new RuleInterpreter().EvaluateAsync(rules, fixture, fixture.Config, fixture.Parameters, TestContext.Current.CancellationToken);

        Assert.Equal(10_000, results.Count);
        Assert.All(results, r => Assert.Equal(RuleStatus.Pass, r.Status));
        Assert.Equal(500, results.Select(r => r.Rule).Distinct(StringComparer.Ordinal).Count());
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5));
    }
}

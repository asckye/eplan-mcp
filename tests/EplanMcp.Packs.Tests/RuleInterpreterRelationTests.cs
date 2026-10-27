using static EplanMcp.Packs.Tests.RuleInterpreterFixture;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class RuleInterpreterRelationTests
{
    [Theory]
    [InlineData("some", "", true, RuleStatus.Fail)]
    [InlineData("every", "", true, RuleStatus.Pass)]
    [InlineData("some", "T", true, RuleStatus.Pass)]
    [InlineData("every", "T", true, RuleStatus.Pass)]
    [InlineData("some", "F", true, RuleStatus.Fail)]
    [InlineData("every", "F", true, RuleStatus.Fail)]
    [InlineData("some", "U", true, RuleStatus.Unknown)]
    [InlineData("every", "U", true, RuleStatus.Unknown)]
    [InlineData("some", "UT", true, RuleStatus.Pass)]
    [InlineData("every", "UF", true, RuleStatus.Fail)]
    [InlineData("some", "UF", true, RuleStatus.Unknown)]
    [InlineData("every", "UT", true, RuleStatus.Unknown)]
    [InlineData("some", "", false, RuleStatus.Unknown)]
    [InlineData("every", "", false, RuleStatus.Unknown)]
    [InlineData("some", "T", false, RuleStatus.Unknown)]
    [InlineData("every", "F", false, RuleStatus.Unknown)]
    public async Task Quantifiers_CoverageAndPredicates_PreserveUnknown(string op, string matches, bool complete, RuleStatus expected)
    {
        RuleInterpreterFixture fixture = Related(matches, complete);
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule($$$$"""{"{{{{op}}}}":{"relation":"function.connections","where":{"eq":[{"property":"connection.name"},{"literal":"T"}]}}}""")));

        Assert.Equal(expected, result.Status);
        Assert.Contains(result.Evidence, e => e.Location.EndsWith("/relation", StringComparison.Ordinal) && e.Complete == complete);
        if (!complete)
        {
            Assert.Contains(result.Evidence, e => e.Note == "Incomplete relation coverage.");
        }
    }

    [Theory]
    [InlineData("", true, 0, RuleStatus.Pass)]
    [InlineData("TFT", true, 2, RuleStatus.Pass)]
    [InlineData("TF", true, 2, RuleStatus.Fail)]
    [InlineData("TU", true, 1, RuleStatus.Unknown)]
    [InlineData("T", false, 1, RuleStatus.Unknown)]
    [InlineData("", false, 0, RuleStatus.Unknown)]
    public async Task Count_FilterAndCoverage_RequireExactKnownCount(string matches, bool complete, int count, RuleStatus expected)
    {
        RuleInterpreterFixture fixture = Related(matches, complete);
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule($$$$"""{"eq":[{"count":{"relation":"function.connections","where":{"eq":[{"property":"connection.name"},{"literal":"T"}]}}},{"literal":{{{{count}}}}}]}""")));

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData("connection.start")]
    [InlineData("connection.end")]
    public async Task Endpoints_RegisteredDirectRelations_UseTargetLocators(string relation)
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Kinds["connection"] = ["c1"];
        fixture.Relations[("c1", relation)] = new(["-M1"], true, "direct endpoint");
        fixture.Properties[("-M1", "function.main")] = Data("true", "boolean");
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule($$$$"""{"some":{"relation":"{{{{relation}}}}","where":{"property":"function.main"}}}""", kind: "connection")));

        Assert.Equal(RuleStatus.Pass, result.Status);
        Assert.Contains(result.Evidence, e => e.Locator == "-M1" && e.Value!.Value.GetBoolean());
    }

    [Fact]
    public async Task Relations_ThreeHopsAndMaximumDepth_EvaluateValidatedTree()
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Relations[("-M1", "function.connections")] = new(["c1"], true);
        fixture.Relations[("c1", "connection.end")] = new(["-M1"], true, Inferred: true);
        RuleDefinition rule = fixture.Rule("""{"some":{"relation":"function.connections","where":{"every":{"relation":"connection.end","where":{"some":{"relation":"function.connections","where":{"not":[{"not":[{"not":[{"not":[{"literal":true}]}]}]}]}}}}}}}""");
        RuleResult result = Assert.Single(await fixture.Evaluate(rule));

        Assert.Equal(RuleStatus.Pass, result.Status);
        Assert.Equal("inference", result.Basis);
    }

    [Fact]
    public async Task EmptySet_FromTypedConfig_IsKnownEmpty()
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Config["choices"] = Data("[]", set: true);
        Assert.Equal(RuleStatus.Pass, Assert.Single(await fixture.Evaluate(fixture.Rule("""{"empty":[{"config":"choices"}]}"""))).Status);
        Assert.Equal(RuleStatus.Fail, Assert.Single(await fixture.Evaluate(fixture.Rule("""{"in":[{"literal":"x"},{"config":"choices"}]}"""))).Status);
    }

    private static RuleInterpreterFixture Related(string matches, bool complete)
    {
        var fixture = new RuleInterpreterFixture();
        string[] targets = [.. matches.Select((_, i) => "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        fixture.Relations[("-M1", "function.connections")] = new(targets, complete, "direct snapshot");
        for (int i = 0; i < targets.Length; i++)
        {
            if (matches[i] != 'U')
            {
                fixture.Properties[(targets[i], "connection.name")] = Data(matches[i] == 'T' ? "\"T\"" : "\"F\"");
            }
        }

        return fixture;
    }
}

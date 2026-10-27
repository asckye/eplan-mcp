using System.Text.Json;
using System.Text.RegularExpressions;
using static EplanMcp.Packs.Tests.RuleInterpreterFixture;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class RuleInterpreterTests
{
    public static TheoryData<string, bool?, bool?, RuleStatus> LogicalCases
    {
        get
        {
            var cases = new TheoryData<string, bool?, bool?, RuleStatus>();
            foreach (bool? left in new bool?[] { false, true, null })
            {
                foreach (bool? right in new bool?[] { false, true, null })
                {
                    cases.Add("all", left, right, left is false || right is false ? RuleStatus.Fail : left is null || right is null ? RuleStatus.Unknown : RuleStatus.Pass);
                    cases.Add("any", left, right, left is true || right is true ? RuleStatus.Pass : left is null || right is null ? RuleStatus.Unknown : RuleStatus.Fail);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(LogicalCases))]
    public async Task Logic_TruthTable_UsesKleeneLogic(string op, bool? left, bool? right, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        string Operand(bool? value) => value.HasValue ? value.Value ? "{\"literal\":true}" : "{\"literal\":false}" : "{\"property\":\"function.main\"}";
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule($$"""{"{{op}}":[{{Operand(left)}},{{Operand(right)}}]}""")));

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(true, RuleStatus.Fail)]
    [InlineData(false, RuleStatus.Pass)]
    [InlineData(null, RuleStatus.Unknown)]
    public async Task Not_TruthTable_PreservesUnknown(bool? value, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        if (value.HasValue)
        {
            fixture.Properties[("-M1", "function.main")] = Data(value.Value ? "true" : "false", "boolean");
        }

        Assert.Equal(expected, Assert.Single(await fixture.Evaluate(fixture.Rule("""{"not":[{"property":"function.main"}]}"""))).Status);
    }

    [Theory]
    [InlineData("""{"eq":[{"literal":1,"unit":"kW"},{"literal":1000,"unit":"W"}]}""", RuleStatus.Pass)]
    [InlineData("""{"ne":[{"literal":1},{"literal":1.0}]}""", RuleStatus.Fail)]
    [InlineData("""{"lt":[{"literal":999,"unit":"mA"},{"literal":1,"unit":"A"}]}""", RuleStatus.Pass)]
    [InlineData("""{"le":[{"literal":1,"unit":"m"},{"literal":1000,"unit":"mm"}]}""", RuleStatus.Pass)]
    [InlineData("""{"gt":[{"literal":2,"unit":"kV"},{"literal":1000,"unit":"V"}]}""", RuleStatus.Pass)]
    [InlineData("""{"ge":[{"literal":1},{"literal":2}]}""", RuleStatus.Fail)]
    [InlineData("""{"in":[{"literal":1,"unit":"kW"},{"literal":[500,1000],"unit":"W"}]}""", RuleStatus.Pass)]
    [InlineData("""{"not_in":[{"literal":"Motor"},{"literal":["motor","Coil"]}]}""", RuleStatus.Pass)]
    [InlineData("""{"in":[{"literal":true},{"literal":[false,true]}]}""", RuleStatus.Pass)]
    [InlineData("""{"eq":[{"literal":"Motor"},{"literal":"motor"}]}""", RuleStatus.Fail)]
    [InlineData("""{"eq":[{"literal":true},{"literal":true}]}""", RuleStatus.Pass)]
    [InlineData("""{"exists":[{"literal":false}]}""", RuleStatus.Pass)]
    [InlineData("""{"empty":[{"literal":""}]}""", RuleStatus.Pass)]
    [InlineData("""{"empty":[{"literal":[false]}]}""", RuleStatus.Fail)]
    [InlineData("""{"starts_with":[{"literal":"=AP+柜-M1"},{"literal":"=AP"}]}""", RuleStatus.Pass)]
    [InlineData("""{"ends_with":[{"literal":"=AP-M1"},{"literal":"-m1"}]}""", RuleStatus.Fail)]
    [InlineData("""{"matches":[{"literal":"=AP-M1"},{"literal":"-M[0-9]+$"}]}""", RuleStatus.Pass)]
    [InlineData("""{"matches":[{"literal":"=AP-K1"},{"literal":"-M[0-9]+$"}]}""", RuleStatus.Fail)]
    public async Task Operators_ValidatedOperands_ReturnExpectedStatus(string check, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        Assert.Equal(expected, Assert.Single(await fixture.Evaluate(fixture.Rule(check))).Status);
    }

    [Theory]
    [InlineData("79228162514264337593543950335", "kW")]
    [InlineData("0.0000000000000000000000000001", "mm")]
    public async Task Units_OverflowOrPrecisionLoss_RemainsUnknown(string number, string unit)
    {
        var fixture = new RuleInterpreterFixture();
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule($$"""{"eq":[{"literal":{{number}},"unit":"{{unit}}"},{"literal":0,"unit":"{{unit}}"}]}""")));

        Assert.Equal(RuleStatus.Unknown, result.Status);
        Assert.Contains(result.Evidence, e => !e.Complete);
    }

    [Theory]
    [InlineData("exists")]
    [InlineData("empty")]
    public async Task MissingProperty_ExistenceAndEmptiness_AreUnknown(string op)
    {
        var fixture = new RuleInterpreterFixture();
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule($$"""{"{{op}}":[{"property":"function.name"}]}""")));

        Assert.Equal(RuleStatus.Unknown, result.Status);
        Assert.Equal("/spec/check/" + op + "/0/property", Assert.Single(result.Evidence).Location);
        Assert.False(result.Evidence[0].Complete);
    }

    [Theory]
    [InlineData("\"Motor\"", "text", false)]
    [InlineData("true", "boolean", true)]
    [InlineData("1e1000", "text", true)]
    public async Task Property_IncompleteOrWrongRuntimeType_CannotPass(string json, string kind, bool complete)
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Properties[("-M1", "function.name")] = Data(json, kind, complete: complete);

        Assert.Equal(RuleStatus.Unknown, Assert.Single(await fixture.Evaluate(fixture.Rule("""{"exists":[{"property":"function.name"}]}"""))).Status);
    }

    [Theory]
    [InlineData("""{"exists":[{"property":"function.name"}]}""", RuleStatus.Fail)]
    [InlineData("""{"empty":[{"property":"function.name"}]}""", RuleStatus.Pass)]
    [InlineData("""{"eq":[{"property":"function.name"},{"literal":"-M1"}]}""", RuleStatus.Unknown)]
    public async Task Property_KnownAbsentWithCompleteCoverage_IsNotMissingData(string check, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Properties[("-M1", "function.name")] = Data("null");

        Assert.Equal(expected, Assert.Single(await fixture.Evaluate(fixture.Rule(check))).Status);
    }

    [Fact]
    public async Task References_ConfigParametersAndEvidence_PreserveTypesAndBasis()
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Config["power"] = Data("1", "number", "kW");
        fixture.Parameters["power"] = Data("1000", "number", "W", inferred: true);
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule("""{"eq":[{"config":"power"},{"parameter":"power"}]}""")));

        Assert.Equal(RuleStatus.Pass, result.Status);
        Assert.Equal("inference", result.Basis);
        Assert.Equal(2, result.Evidence.Count);
        Assert.All(result.Evidence, e => Assert.Equal("snapshot evidence", e.Note));
        Assert.Equal("1", result.Evidence[0].Value!.Value.GetRawText());
        Assert.Equal("-M1", result.Locator);
        Assert.Equal("rule/test", result.Rule);
        Assert.Equal("1.2.3", result.Version);
        using JsonDocument document = JsonDocument.Parse(PackJson.Serialize(result));
        Assert.Equal("pass", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(6, document.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("config")]
    [InlineData("parameter")]
    public async Task References_ValueMissingAfterValidation_RemainsUnknown(string op)
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Config["value"] = Data("true", "boolean");
        fixture.Parameters["value"] = Data("true", "boolean");
        RuleDefinition rule = fixture.Rule($$"""{"{{op}}":"value"}""");
        fixture.Config.Clear();
        fixture.Parameters.Clear();

        Assert.Equal(RuleStatus.Unknown, Assert.Single(await fixture.Evaluate(rule)).Status);
    }

    [Theory]
    [InlineData("{\"literal\":false}", RuleStatus.NotApplicable)]
    [InlineData("{\"property\":\"function.main\"}", RuleStatus.Unknown)]
    [InlineData("{\"literal\":true}", RuleStatus.Pass)]
    public async Task Scope_FalseUnknownOrTrue_GatesCheck(string scope, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Properties[("-M1", "function.name")] = Data("\"Motor\"");
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule("""{"exists":[{"property":"function.name"}]}""", scope)));

        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == RuleStatus.Pass ? 1 : expected == RuleStatus.Unknown ? 1 : 0, fixture.Reads);
        Assert.DoesNotContain(result.Evidence, e => expected != RuleStatus.Pass && e.Location.StartsWith("/spec/check", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scope_ObjectKind_SelectsOnlyThatKind()
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Kinds["page"] = ["+A1/1", "+A1/2"];
        IReadOnlyList<RuleResult> results = await fixture.Evaluate(fixture.Rule("{\"literal\":true}", kind: "page"));

        Assert.Equal(fixture.Kinds["page"], results.Select(r => r.Locator));
        Assert.All(results, r => Assert.Equal("fact", r.Basis));
        Assert.Empty(await fixture.Evaluate(fixture.Rule("{\"literal\":true}", kind: "connection")));
    }

    [Theory]
    [InlineData(2000, RuleStatus.Pass)]
    [InlineData(2001, RuleStatus.Unknown)]
    public async Task Regex_DynamicInputLimit_IsEnforced(int length, RuleStatus expected)
    {
        var fixture = new RuleInterpreterFixture();
        fixture.Properties[("-M1", "function.name")] = Data(JsonSerializer.Serialize(new string('a', length)));
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule("""{"matches":[{"property":"function.name"},{"literal":"^a+$"}]}""")));

        Assert.Equal(expected, result.Status);
        if (expected == RuleStatus.Unknown)
        {
            Assert.Contains(result.Evidence, e => e.Note == "Regex input limit exceeded.");
        }
    }

    [Fact]
    public async Task Regex_Timeout_IsUnknownWithCoverage()
    {
        var fixture = new RuleInterpreterFixture();
        var interpreter = new RuleInterpreter(new ManualRuleClock())
        {
            Match = (input, pattern) => throw new RegexMatchTimeoutException(input, pattern, RuleValidator.RegexTimeout),
        };
        RuleResult result = Assert.Single(await fixture.Evaluate(fixture.Rule("""{"matches":[{"literal":"a"},{"literal":"a"}]}"""), interpreter));

        Assert.Equal(RuleStatus.Unknown, result.Status);
        Assert.Contains(result.Evidence, e => !e.Complete && e.Note == "Regex timeout exceeded.");
        Assert.Equal(RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, RuleValidator.PatternOptions);
        Assert.Equal(TimeSpan.FromMilliseconds(20), RuleValidator.RegexTimeout);
    }
}

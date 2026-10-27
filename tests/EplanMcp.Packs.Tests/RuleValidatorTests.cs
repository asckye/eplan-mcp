using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class RuleValidatorTests
{
    [Theory]
    [InlineData("eq", """[{"property":"function.category"},{"literal":"Motor"}]""")]
    [InlineData("ne", """[{"property":"function.main"},{"literal":false}]""")]
    [InlineData("lt", """[{"literal":1},{"literal":2}]""")]
    [InlineData("le", """[{"literal":1},{"literal":2}]""")]
    [InlineData("gt", """[{"literal":1,"unit":"kW"},{"literal":2,"unit":"W"}]""")]
    [InlineData("ge", """[{"literal":1,"unit":"m"},{"literal":2,"unit":"mm"}]""")]
    [InlineData("in", """[{"property":"function.category"},{"literal":["Motor","Coil"]}]""")]
    [InlineData("not_in", """[{"literal":1,"unit":"V"},{"literal":[2,3],"unit":"kV"}]""")]
    [InlineData("exists", """[{"property":"function.name"}]""")]
    [InlineData("empty", """[{"literal":""}]""")]
    [InlineData("empty", """[{"literal":[true,false]}]""")]
    [InlineData("starts_with", """[{"property":"function.name"},{"literal":"=AP"}]""")]
    [InlineData("ends_with", """[{"property":"function.name"},{"literal":"-M1"}]""")]
    [InlineData("matches", """[{"property":"function.name"},{"literal":"^.*-M[0-9]+$"}]""")]
    [InlineData("all", """[{"literal":true},{"literal":false}]""")]
    [InlineData("any", """[{"literal":true},{"literal":false}]""")]
    [InlineData("not", """[{"literal":true}]""")]
    [InlineData("some", """{"relation":"function.connections","where":{"exists":[{"property":"connection.name"}]}}""")]
    [InlineData("every", """{"relation":"function.connections","where":{"literal":true}}""")]
    public void Operator_RegisteredContract_ProducesTypedPredicate(string op, string operands)
    {
        RuleNode node = Check($$"""{"{{op}}":{{operands}}}""").Check;

        Assert.Equal(op, node.Operator);
        Assert.Equal(new RuleValueType("boolean"), node.Type);
        Assert.NotEmpty(node.Operands);
    }

    [Theory]
    [InlineData("\"function.category == Motor\"", "/spec/check")]
    [InlineData("null", "/spec/check")]
    [InlineData("{}", "/spec/check")]
    [InlineData("{\"unit\":\"W\"}", "/spec/check")]
    [InlineData("{\"eq\":[],\"ne\":[]}", "/spec/check")]
    [InlineData("{\"script\":\"return true;\"}", "/spec/check/script")]
    [InlineData("{\"a~/b\":true}", "/spec/check/a~0~1b")]
    [InlineData("{\"add\":[1,2]}", "/spec/check/add")]
    [InlineData("{\"eq\":[\"function.category\",\"Motor\"]}", "/spec/check/eq/0")]
    [InlineData("{\"eq\":{}}", "/spec/check/eq")]
    [InlineData("{\"eq\":[{\"literal\":true}]}", "/spec/check/eq")]
    [InlineData("{\"not\":[{\"literal\":true},{\"literal\":true}]}", "/spec/check/not")]
    [InlineData("{\"all\":[]}", "/spec/check/all")]
    [InlineData("{\"any\":[]}", "/spec/check/any")]
    [InlineData("{\"object\":true}", "/spec/check/object")]
    [InlineData("{\"literal\":1}", "/spec/check")]
    [InlineData("{\"exists\":[{\"literal\":null}]}", "/spec/check/exists/0/literal")]
    [InlineData("{\"exists\":[{\"literal\":{\"code\":\"x\"}}]}", "/spec/check/exists/0/literal")]
    [InlineData("{\"exists\":[{\"literal\":[]}]}", "/spec/check/exists/0/literal")]
    [InlineData("{\"exists\":[{\"literal\":1e1000}]}", "/spec/check/exists/0/literal")]
    [InlineData("{\"exists\":[{\"property\":1}]}", "/spec/check/exists/0/property")]
    public void Expression_InvalidForm_IsRefusedAtPointer(string expression, string expectedPointer) => Problem(expression, expectedPointer);

    [Theory]
    [InlineData("{\"eq\":[{\"literal\":1},{\"literal\":\"1\"}]}", "/spec/check/eq")]
    [InlineData("{\"ne\":[{\"literal\":true},{\"literal\":1}]}", "/spec/check/ne")]
    [InlineData("{\"lt\":[{\"literal\":\"a\"},{\"literal\":\"b\"}]}", "/spec/check/lt")]
    [InlineData("{\"all\":[{\"literal\":true},{\"literal\":1}]}", "/spec/check/all")]
    [InlineData("{\"not\":[{\"literal\":\"true\"}]}", "/spec/check/not")]
    [InlineData("{\"empty\":[{\"literal\":false}]}", "/spec/check/empty")]
    [InlineData("{\"in\":[{\"literal\":\"a\"},{\"literal\":\"abc\"}]}", "/spec/check/in")]
    [InlineData("{\"not_in\":[{\"literal\":1},{\"literal\":[1,\"a\"]}]}", "/spec/check/not_in/1/literal")]
    [InlineData("{\"in\":[{\"literal\":1},{\"literal\":[true,false]}]}", "/spec/check/in")]
    [InlineData("{\"eq\":[{\"literal\":[1]},{\"literal\":[1]}]}", "/spec/check/eq")]
    [InlineData("{\"starts_with\":[{\"literal\":1},{\"literal\":\"a\"}]}", "/spec/check/starts_with")]
    [InlineData("{\"eq\":[{\"literal\":1,\"unit\":\"W\"},{\"literal\":1,\"unit\":\"V\"}]}", "/spec/check/eq")]
    [InlineData("{\"eq\":[{\"literal\":1,\"unit\":\"W\"},{\"literal\":1}]}", "/spec/check/eq")]
    [InlineData("{\"exists\":[{\"literal\":1,\"unit\":\"hp\"}]}", "/spec/check/exists/0/unit")]
    [InlineData("{\"exists\":[{\"literal\":\"1\",\"unit\":\"W\"}]}", "/spec/check/exists/0/unit")]
    [InlineData("{\"exists\":[{\"property\":\"function.name\",\"unit\":\"W\"}]}", "/spec/check/exists/0/unit")]
    [InlineData("{\"some\":{\"relation\":\"function.connections\",\"where\":{\"literal\":1}}}", "/spec/check/some/where")]
    public void Operands_MismatchedTypesOrDimensions_AreRefused(string expression, string expectedPointer) => Problem(expression, expectedPointer);

    [Theory]
    [InlineData("{\"exists\":[{\"property\":\"function.unregistered\"}]}", "/spec/check/exists/0/property")]
    [InlineData("{\"exists\":[{\"property\":\"page.name\"}]}", "/spec/check/exists/0/property")]
    [InlineData("{\"exists\":[{\"property\":\"function.20027\"}]}", "/spec/check/exists/0/property")]
    [InlineData("{\"exists\":[{\"config\":\"motor.missing\"}]}", "/spec/check/exists/0/config")]
    [InlineData("{\"exists\":[{\"parameter\":\"power\"}]}", "/spec/check/exists/0/parameter")]
    [InlineData("{\"some\":{\"relation\":\"same_potential\",\"where\":{\"literal\":true}}}", "/spec/check/some/relation")]
    [InlineData("{\"some\":{\"relation\":\"connection.start\",\"where\":{\"literal\":true}}}", "/spec/check/some/relation")]
    [InlineData("{\"every\":{\"relation\":1,\"where\":{\"literal\":true}}}", "/spec/check/every/relation")]
    [InlineData("{\"count\":{\"relation\":\"function.connections\",\"where\":{\"literal\":true},\"hops\":9}}", "/spec/check/count/hops")]
    public void Registration_UnknownOrWrongContext_IsRefused(string expression, string expectedPointer) => Problem(expression, expectedPointer);

    [Fact]
    public void Count_DirectRelations_ProducesDimensionlessNumber()
    {
        RuleNode node = Check("""{"ge":[{"count":{"relation":"function.connections","where":{"literal":true}}},{"literal":1}]}""").Check;

        Assert.Equal(new RuleValueType("number"), node.Operands[0].Type);
    }

    [Theory]
    [InlineData("connection", "connection.start")]
    [InlineData("connection", "connection.end")]
    public void Relation_DirectEndpoint_UsesFunctionPropertyContext(string kind, string relation)
    {
        _ = Check("""{"some":{"relation":"RELATION","where":{"exists":[{"property":"function.category"}]}}}""".Replace("RELATION", relation, StringComparison.Ordinal), kind);
    }

    [Theory]
    [InlineData("page", "page.name", "text")]
    [InlineData("page", "page.type", "text")]
    [InlineData("connection", "connection.name", "text")]
    [InlineData("function", "function.main", "boolean")]
    public void Registry_CoveredProperty_ExposesTypeCoverageAndMissingSemantics(string kind, string property, string type)
    {
        RuleNode node = Check($$"""{"exists":[{"property":"{{property}}"}]}""", kind).Check.Operands[0];

        Assert.Equal(new RuleValueType(type), node.Type);
        Assert.StartsWith("snapshot.", RuleValidator.Properties[property].Coverage, StringComparison.Ordinal);
        Assert.Equal(RuleStatus.Unknown, RuleValidator.MissingFactStatus);
    }

    [Fact]
    public void References_ExplicitTypeContext_IsUsedWithoutCoercion()
    {
        JsonElement spec = Spec("""{"ge":[{"config":"motor.power"},{"parameter":"power"}]}""");
        RuleNode node = RuleValidator.Validate(spec, name => name == "motor.power" ? new("number", "kW") : null,
            name => name == "power" ? new("number", "W") : null).Check;

        Assert.Equal("kW", node.Operands[0].Type.Unit);
        Assert.Equal("W", node.Operands[1].Type.Unit);
        Assert.Throws<PackFormatException>(() => RuleValidator.Validate(spec, _ => new("number", "kW"), _ => new("text")));
        Assert.Throws<PackFormatException>(() => RuleValidator.Validate(spec, _ => new("number", "hp"), _ => new("number")));
    }

    [Theory]
    [InlineData("[")]
    [InlineData("(?=a)a")]
    [InlineData("(a)\\1")]
    public void Matches_InvalidOrBacktrackingOnlyPattern_IsRefused(string pattern)
    {
        Problem($$"""{"matches":[{"property":"function.name"},{"literal":{{JsonSerializer.Serialize(pattern)}}}]}""", "/spec/check/matches/1");
    }

    [Fact]
    public void Matches_PatternAndInputLimits_AreInclusive()
    {
        string pattern = new('a', RuleValidator.MaxPatternLength);
        string input = new('a', RuleValidator.MaxInputLength);
        string Expression(string text, string regex) => $$"""{"matches":[{"literal":"{{text}}"},{"literal":"{{regex}}"}]}""";

        _ = Check(Expression(input, pattern));
        Problem(Expression(input, pattern + "a"), "/spec/check/matches/1");
        Problem(Expression(input + "a", pattern), "/spec/check/matches/0");
        Problem("""{"matches":[{"property":"function.name"},{"property":"function.category"}]}""", "/spec/check/matches/1");
        Assert.True(RuleValidator.PatternOptions.HasFlag(System.Text.RegularExpressions.RegexOptions.NonBacktracking));
        Assert.Equal(TimeSpan.FromMilliseconds(20), RuleValidator.RegexTimeout);
    }

    [Fact]
    public void Depth_EightNodesAccepted_NinthRefused()
    {
        string expression = """{"literal":true}""";
        for (int i = 1; i < RuleValidator.MaxDepth; i++)
        {
            expression = "{\"not\":[" + expression + "]}";
        }

        _ = Check(expression);
        Assert.Contains("depth", Assert.Throws<PackFormatException>(() => Check("{\"not\":[" + expression + "]}")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nodes_EntireRuleAndSetEntries_CountTowardLimit()
    {
        string expression = "{\"all\":[" + string.Join(',', Enumerable.Repeat("{\"literal\":true}", RuleValidator.MaxNodes - 2)) + "]}";
        _ = Check(expression);
        Assert.Contains("limit", Assert.Throws<PackFormatException>(() => Check(expression.Replace("]}", ",{\"literal\":true}]}", StringComparison.Ordinal))).Message, StringComparison.Ordinal);
        string Set(int size) => "{\"in\":[{\"literal\":1},{\"literal\":[" + string.Join(',', Enumerable.Repeat("1", size)) + "]}]}";
        _ = Check(Set(RuleValidator.MaxNodes - 4));
        Problem(Set(RuleValidator.MaxNodes - 3), "/spec/check/in/1/literal");
    }

    [Fact]
    public void Relations_ThreeHopsAccepted_FourthRefused()
    {
        string expression = """{"every":{"relation":"function.connections","where":{"some":{"relation":"connection.end","where":{"some":{"relation":"function.connections","where":{"literal":true}}}}}}}""";
        _ = Check(expression);
        expression = expression.Replace("{\"literal\":true}", "{\"some\":{\"relation\":\"connection.start\",\"where\":{\"literal\":true}}}", StringComparison.Ordinal);

        Assert.Contains("3 hops", Assert.Throws<PackFormatException>(() => Check(expression)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/spec/extra", "extra", "true")]
    [InlineData("/spec/severity", "severity", "\"critical\"")]
    [InlineData("/spec/source", "source", "null")]
    [InlineData("/spec/scope/kind", "scope", "{\"kind\":\"terminal\",\"where\":{\"literal\":true}}")]
    [InlineData("/spec/scope/where", "scope", "{\"kind\":\"function\",\"where\":{\"literal\":1}}")]
    [InlineData("/spec/message", "message", "\"message\"")]
    [InlineData("/spec/message/a~0~1b", "message", "{\"a~/b\":null}")]
    [InlineData("/spec/fix/action", "fix", "{\"workflow\":\"workflow/fix\",\"inputs\":{},\"action\":\"run\"}")]
    [InlineData("/spec/fix/inputs/target", "fix", "{\"workflow\":\"workflow/fix\",\"inputs\":{\"target\":\"$object\"}}")]
    [InlineData("/spec/fix/inputs/target/object", "fix", "{\"workflow\":\"workflow/fix\",\"inputs\":{\"target\":{\"object\":false}}}")]
    public void Spec_IllegalMemberOrValue_ReportsPointer(string expectedPointer, string key, string json)
    {
        JsonObject spec = JsonNode.Parse(Spec("""{"literal":true}""").GetRawText())!.AsObject();
        spec[key] = JsonNode.Parse(json);

        Assert.StartsWith(expectedPointer + ":", Assert.Throws<PackFormatException>(() => RuleValidator.Validate(JsonSerializer.SerializeToElement(spec))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ast_ClonesDataAndPreservesLiteralStringsAfterDocumentDisposal()
    {
        RuleNode node = Check("""{"eq":[{"literal":"System.IO.File.Delete(path)"},{"literal":"x"}]}""").Check;

        Assert.Equal("System.IO.File.Delete(path)", node.Operands[0].Value.GetString());
    }

    [Theory]
    [InlineData(RuleStatus.Pass, "pass")]
    [InlineData(RuleStatus.Fail, "fail")]
    [InlineData(RuleStatus.Unknown, "unknown")]
    [InlineData(RuleStatus.NotApplicable, "not_applicable")]
    public void Status_SerializesUsingDesignName(RuleStatus status, string name) =>
        Assert.Equal("\"" + name + "\"", JsonSerializer.Serialize(status, PackJson.Options));

    private static (RuleNode Scope, RuleNode Check) Check(string expression, string kind = "function") => RuleValidator.Validate(Spec(expression, kind));

    private static JsonElement Spec(string expression, string kind = "function")
    {
        using JsonDocument document = JsonDocument.Parse($$$"""{"severity":"recommended","source":{"asset":"document/guide"},"scope":{"kind":"{{{kind}}}","where":{"literal":true}},"check":{{{expression}}},"message":{"en":"Check failed"}}""");
        return document.RootElement.Clone();
    }

    private static void Problem(string expression, string expectedPointer) =>
        Assert.StartsWith(expectedPointer + ":", Assert.Throws<PackFormatException>(() => Check(expression)).Message, StringComparison.Ordinal);
}

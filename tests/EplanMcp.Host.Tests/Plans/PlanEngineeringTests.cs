using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanEngineeringTests
{
    internal const string Engineering = """
        {"cabinet":"default","configValuesAndTrace":[{"key":"motor.power","value":7.500,"unit":"kW",
         "constraints":{"allowed":{"max":10,"min":0.5}},"trace":[{"layer":"cabinet","source":{"revision":7},
         "pointer":"/cabinets/0/values/motor.power","kind":"default","value":7.5}]}],
         "configDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
         "workflowRefAndInputs":{"workflow":"workflow/motor","inputs":{"power":7.5}},
         "moduleBindings":[{"parameters":{"power":7.5}}],"ruleDigests":{"motor":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},
         "acceptance":{"checks":[{"expected":7.5}]}}
        """;

    private const string CanonicalEngineering = """{"acceptance":{"checks":[{"expected":7.5}]},"cabinet":"default","configDigest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","configValuesAndTrace":[{"constraints":{"allowed":{"max":10,"min":0.5}},"key":"motor.power","trace":[{"kind":"default","layer":"cabinet","pointer":"/cabinets/0/values/motor.power","source":{"revision":7},"value":7.5}],"unit":"kW","value":7.5}],"moduleBindings":[{"parameters":{"power":7.5}}],"ruleDigests":{"motor":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},"workflowRefAndInputs":{"inputs":{"power":7.5},"workflow":"workflow/motor"}}""";

    internal static PlanDefinition Definition() => PlanTestData.Definition() with
    {
        SchemaVersion = 2,
        CompilerVersion = "3",
        Engineering = PlanJson.Read<PlanEngineering>(Engineering),
    };

    [Fact]
    public void Schema2_CanonicalForm_CoversTypedEngineeringWithExistingAlgorithm()
    {
        PlanDefinition definition = Definition();
        string expected = PlanCanonical.Serialize(PlanTestData.Definition())
            .Replace("\"compilerVersion\":\"1\"", "\"compilerVersion\":\"3\"", StringComparison.Ordinal)
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)
            .Replace("\"eplanVariant\":", "\"engineering\":" + CanonicalEngineering + ",\"eplanVariant\":", StringComparison.Ordinal);

        Assert.Equal(expected, PlanCanonical.Serialize(definition));
        Assert.Equal(expected, PlanCanonical.Serialize(PlanCanonical.ReadDefinition(expected)));
        Assert.Equal(7.5m, Assert.Single(definition.Engineering!.ConfigValuesAndTrace).Value.GetDecimal());
        Assert.Equal(expected, PlanCanonical.Serialize(PlanCanonical.ReadDefinition(JsonSerializer.Serialize(definition, PlanJson.Options))));
    }

    [Theory]
    [InlineData("1", "5e836655d28299139991e155662f4632e4c948e5de56a2415b86fba3af9944fe")]
    [InlineData("2", "9b942bc8888dd76ab50fceda4805ecaa38bf59e2f634a993a8bb2b3e0cae2aef")]
    public void Schema1_FixedHashesAndAbsentEngineering_ArePreserved(string compiler, string hash)
    {
        PlanDefinition definition = PlanTestData.Definition() with { CompilerVersion = compiler };
        string json = JsonSerializer.Serialize(definition, PlanJson.Options);
        PlanDefinition read = PlanCanonical.ReadDefinition(json);

        Assert.DoesNotContain("engineering", json, StringComparison.Ordinal);
        Assert.Null(read.Engineering);
        Assert.Equal(hash, PlanCanonical.Hash(read));
        Assert.Equal(PlanCanonical.Serialize(definition), PlanCanonical.Serialize(read));
        Assert.Equal("2", WorkflowCompilation.CompilerVersion);
    }

    [Theory]
    [InlineData("cabinet", "\"a2\"")]
    [InlineData("configValuesAndTrace", "[]")]
    [InlineData("configDigest", "\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"")]
    [InlineData("workflowRefAndInputs", "{\"inputs\":{\"power\":8}}")]
    [InlineData("moduleBindings", "[{\"parameters\":{\"power\":8}}]")]
    [InlineData("ruleDigests", "{}")]
    [InlineData("acceptance", "{\"checks\":[{\"expected\":8}]}")]
    public void Engineering_EveryField_ParticipatesInHash(string field, string replacement)
    {
        PlanDefinition definition = Definition();
        JsonNode node = JsonNode.Parse(JsonSerializer.Serialize(definition, PlanJson.Options))!;
        node["engineering"]![field] = JsonNode.Parse(replacement);
        PlanDefinition changed = PlanCanonical.ReadDefinition(node.ToJsonString());

        Assert.NotEqual(PlanCanonical.Hash(definition), PlanCanonical.Hash(changed));
    }

    [Theory]
    [InlineData("\"cabinet\":\"default\"", "\"cabinet\":\"default\",\"future/~\":true", "/engineering/future~1~0")]
    [InlineData("\"cabinet\":\"default\"", "\"cabinet\":null", "/engineering/cabinet")]
    [InlineData("\"configDigest\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", "\"configDigest\":\"bad\"", "/engineering/configDigest")]
    [InlineData("\"key\":\"motor.power\"", "\"key\":\"motor.power\",\"extra\":0", "/engineering/configValuesAndTrace/0/extra")]
    [InlineData("\"kind\":\"default\"", "\"kind\":\"default\",\"extra\":0", "/engineering/configValuesAndTrace/0/trace/0/extra")]
    [InlineData("\"value\":7.500", "\"value\":{}", "/engineering/configValuesAndTrace/0/value")]
    [InlineData("\"source\":{\"revision\":7}", "\"source\":[]", "/engineering/configValuesAndTrace/0/trace/0/source")]
    [InlineData("\"workflowRefAndInputs\":{\"workflow\":\"workflow/motor\",\"inputs\":{\"power\":7.5}}", "\"workflowRefAndInputs\":[]", "/engineering/workflowRefAndInputs")]
    [InlineData("\"moduleBindings\":[{\"parameters\":{\"power\":7.5}}]", "\"moduleBindings\":[false]", "/engineering/moduleBindings/0")]
    [InlineData("\"acceptance\":{\"checks\":[{\"expected\":7.5}]}", "\"acceptance\":null", "/engineering/acceptance")]
    [InlineData("\"expected\":7.5", "\"expected\":1e1000", "/engineering/acceptance/checks/0/expected")]
    [InlineData("\"cabinet\":\"default\",", "", "/engineering/cabinet")]
    public void Engineering_InvalidMembersShapesOrNumbers_RejectWithPointer(string original, string replacement, string problem)
    {
        Assert.Contains(original, Engineering, StringComparison.Ordinal);
        string json = "{\"schemaVersion\":2,\"compilerVersion\":\"3\",\"engineering\":"
            + Engineering.Replace(original, replacement, StringComparison.Ordinal) + "}";

        JsonException error = Assert.Throws<JsonException>(() => PlanJson.Read<PlanDefinition>(json));
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "3")]
    [InlineData(2, "1")]
    [InlineData(2, "2")]
    [InlineData(2, "4")]
    [InlineData(3, "3")]
    public void Read_UnknownSchemaOrCompilerCombination_Rejects(int schema, string compiler)
    {
        PlanDefinition definition = Definition() with { SchemaVersion = schema, CompilerVersion = compiler };
        Assert.Throws<NotSupportedException>(() => PlanJson.Read<PlanDefinition>(JsonSerializer.Serialize(definition, PlanJson.Options)));
        Assert.Throws<NotSupportedException>(() => PlanCanonical.Hash(definition));
    }

    [Theory]
    [InlineData(1, "1", "null")]
    [InlineData(1, "2", "{}")]
    [InlineData(2, "3", "null")]
    public void Engineering_InvalidForVersion_RejectsEvenExplicitNull(int schema, string compiler, string engineering)
    {
        string json = JsonSerializer.Serialize(PlanTestData.Definition() with { SchemaVersion = schema, CompilerVersion = compiler }, PlanJson.Options);
        JsonException error = Assert.Throws<JsonException>(() => PlanJson.Read<PlanDefinition>("{\"engineering\":" + engineering + "," + json[1..]));
        Assert.Contains("/engineering", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema2_MissingEngineeringOrMalformedTypedWrite_Rejects()
    {
        PlanDefinition missing = Definition() with { Engineering = null };
        Assert.Throws<JsonException>(() => PlanJson.Read<PlanDefinition>(JsonSerializer.Serialize(missing, PlanJson.Options)));
        Assert.Throws<JsonException>(() => PlanCanonical.Hash(missing));
        Assert.Throws<JsonException>(() => PlanCanonical.Hash(Definition() with
        {
            Engineering = Definition().Engineering! with { Acceptance = PlanTestData.Json("[]") },
        }));
        Assert.Throws<JsonException>(() => PlanCanonical.Hash(PlanTestData.Definition() with { Engineering = Definition().Engineering }));
    }

    [Fact]
    public void Engineering_DuplicateMembersAndRuntimeIdentifiers_Reject()
    {
        string json = JsonSerializer.Serialize(Definition(), PlanJson.Options);
        Assert.Throws<JsonException>(() => PlanJson.Read<PlanDefinition>(json.Replace("\"expected\":7.5", "\"expected\":7.5,\"expected\":8", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => PlanCanonical.ReadDefinition(json.Replace("\"expected\":7.5", "\"DatabaseIdentifier\":42", StringComparison.Ordinal)));
    }
}

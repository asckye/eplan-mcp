using System.Text;
using System.Text.Json;
using EplanMcp.Host.Plans;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanCanonicalTests
{
    private const string CanonicalDefinition = """{"bindingRevision":null,"cabinet":"default","compilerVersion":"1","contextRef":null,"createdAt":"2026-10-23T00:00:00.0000000Z","digests":{"changesDigest":"changes","names":"names","objects":"objects","pages":"pages"},"eplanVariant":"Pro Panel","eplanVersion":"2026","expiresAt":"2026-10-24T00:00:00.0000000Z","inputs":{"steps":[{"args":{"description":"Example","language":"en_US","page":"+A/1"},"op":"page.update"}]},"lockDigest":null,"owner":{"runId":"host","sessionId":"session"},"planId":"0123456789abcdef0123456789abcdef","project":{"guid":null,"name":"Demo","path":"PROJECT"},"requiredOperations":["page.update"],"resources":[{"exactVersion":"1.0.0","fileHash":"file","manifestDigest":"manifest","pack":"example","resourcePath":"macros/example.ema"}],"schemaVersion":1,"snapshotFormatVersion":1,"sources":[{"exactVersion":null,"fileHash":null,"hash":"object","id":"s1","kind":"page","manifestDigest":null,"oid":42,"pack":null,"project":"PROJECT","resourcePath":null,"type":1}],"steps":[{"args":{"description":"Example","language":"en_US","page":"+A/1"},"coverage":null,"dependsOn":[],"details":{},"effective":true,"expected":[{"description":"Example","source":"s1"}],"index":0,"modifyScope":{"locators":[{"category":null,"kind":"page","name":"+A/1","oid":42,"page":"+A/1","pos":null,"project":"PROJECT","type":1}],"pages":["+A/1"],"structures":["+A"]},"op":"page.update","plannedChanges":[{"action":"update","kind":"page","name":"+A/1","newValue":"Example","oldValue":null,"property":"description"}],"preconditions":[{"hash":"object","locator":{"category":null,"kind":"page","name":"+A/1","oid":42,"page":"+A/1","pos":null,"project":"PROJECT","type":1},"tag":"objectHash"}],"prediction":null,"readScope":{"locators":[{"category":null,"kind":"page","name":"+A/1","oid":42,"page":"+A/1","pos":null,"project":"PROJECT","type":1}],"pages":["+A/1"],"structures":["+A"]},"tier":"write"}]}""";

    [Fact]
    public void Definition_FixedVector_HasExplicitNullsDefaultsAndUtcTimes()
    {
        PlanDefinition definition = PlanTestData.Definition();
        Assert.Equal(CanonicalDefinition, PlanCanonical.Serialize(definition));
        Assert.Equal(CanonicalDefinition, PlanCanonical.Serialize(PlanCanonical.ReadDefinition(CanonicalDefinition)));
        Assert.Equal("5e836655d28299139991e155662f4632e4c948e5de56a2415b86fba3af9944fe", PlanCanonical.Hash(definition));
        byte[] bytes = Encoding.UTF8.GetBytes(PlanCanonical.Serialize(definition));
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal((byte)'}', bytes[^1]);
    }

    [Fact]
    public void Scalars_UnicodeEscapingNumbersAndNull_AgreeAcrossSerializers()
    {
        const string input = """{"z":-0.00E+2,"u":"电😀é<>&/\u2028","b":[1.2300,1e-7,100.00],"a":null}""";
        const string expected = "{\"a\":null,\"b\":[1.23,0.0000001,100],\"u\":\"电😀é<>&/\u2028\",\"z\":0}";
        Assert.Equal(expected, Canonical(input));
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(input))
        {
            FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Decimal,
            DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
        };
        string other = JToken.ReadFrom(reader).ToString(Newtonsoft.Json.Formatting.None);
        Assert.Equal(expected, Canonical(other));
        Assert.Equal(CanonicalDefinition, PlanCanonical.Serialize(PlanCanonical.ReadDefinition(JToken.Parse(CanonicalDefinition).ToString(Newtonsoft.Json.Formatting.None))));
    }

    [Fact]
    public void Strings_FixedControlEscapes_LeaveOtherUnicodeUntouched()
    {
        const string input = """["\u0000\b\t\n\f\r\u000b\u001f\"\\\/é😀"]""";
        const string expected = """["\u0000\b\t\n\f\r\u000b\u001f\"\\/é😀"]""";
        Assert.Equal(expected, Canonical(input));
        Assert.NotEqual(Canonical("\"é\""), Canonical("\"e\u0301\""));
    }

    [Theory]
    [InlineData("-0", "0")]
    [InlineData("-0.000e-2000", "0")]
    [InlineData("1.2300", "1.23")]
    [InlineData("1e+3", "1000")]
    [InlineData("1.2e-3", "0.0012")]
    [InlineData("100.0100", "100.01")]
    [InlineData("1e-28", "0.0000000000000000000000000001")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
    public void Numbers_FixedVectors_AreExactDecimalWithoutExponentOrTrailingZeros(string input, string expected) =>
        Assert.Equal(expected, Canonical(input));

    [Fact]
    public void Numbers_LargeExponentsCancelledByCoefficientZeros_RemainRepresentable()
    {
        Assert.Equal("1", Canonical("1" + new string('0', 1500) + "e-1500"));
        Assert.Equal("1", Canonical("0." + new string('0', 1500) + "1e1501"));
    }

    [Theory]
    [InlineData("1e1000")]
    [InlineData("1e-29")]
    [InlineData("1e-2147483648")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Numbers_OutsideDecimalOrInexact_Reject(string input) => Assert.Throws<JsonException>(() => Canonical(input));

    [Fact]
    public void Keys_RecursivelySortByUtf16Ordinal_WhileArraysKeepOrder()
    {
        const string input = "{\"\uE000\":0,\"😀\":0,\"a\":[{\"z\":1,\"A\":2},3,2,1],\"Z\":null}";
        const string expected = "{\"Z\":null,\"a\":[{\"A\":2,\"z\":1},3,2,1],\"😀\":0,\"\uE000\":0}";
        Assert.Equal(expected, Canonical(input));
        Assert.Throws<JsonException>(() => Canonical("""{"a":1,"\u0061":2}"""));
    }

    [Fact]
    public void ContractSets_DeduplicateAndSort_WithoutSortingStepOrArgumentArrays()
    {
        PlanDefinition definition = PlanTestData.Definition();
        PlanStep step = definition.Steps[0];
        PlanScope scope = step.ReadScope with { Pages = ["z", "a", "a"], Structures = ["z", "A", "A"], Locators = [.. step.ReadScope.Locators, .. step.ReadScope.Locators] };
        PlanDefinition changed = definition with
        {
            RequiredOperations = ["z", "a", "a"],
            Sources = [.. definition.Sources, .. definition.Sources],
            Steps = [step with { Index = 9, Args = PlanTestData.Json("""{"pages":["z","a","a"]}"""), ReadScope = scope }, step],
        };
        PlanDefinition copy = PlanCanonical.ReadDefinition(PlanCanonical.Serialize(changed));
        string[] sortedPages = ["a", "z"];
        string[] sortedStructures = ["A", "z"];
        int[] stepOrder = [9, 0];
        Assert.Equal(sortedPages, copy.RequiredOperations);
        Assert.Single(copy.Sources);
        Assert.Equal(sortedPages, copy.Steps[0].ReadScope.Pages);
        Assert.Equal(sortedStructures, copy.Steps[0].ReadScope.Structures);
        Assert.Single(copy.Steps[0].ReadScope.Locators);
        Assert.Equal(stepOrder, copy.Steps.Select(s => s.Index));
        Assert.Equal("""["z","a","a"]""", copy.Steps[0].Args.GetProperty("pages").GetRawText());
        Assert.Equal(3, changed.RequiredOperations.Length);
    }

    [Fact]
    public void Times_EqualInstantsUseSevenFractionalDigitsAndZ()
    {
        PlanDefinition definition = PlanTestData.Definition();
        Assert.Equal(PlanCanonical.Hash(definition), PlanCanonical.Hash(definition with
        {
            CreatedAt = definition.CreatedAt.ToOffset(TimeSpan.FromHours(8)),
            ExpiresAt = definition.ExpiresAt.ToOffset(TimeSpan.FromHours(-7)),
        }));
        Assert.Contains("2026-10-23T00:00:00.0000001Z", PlanCanonical.Serialize(definition with { CreatedAt = definition.CreatedAt.AddTicks(1) }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"\\uD800\"")]
    [InlineData("\"\\uDC00\"")]
    [InlineData("\"x\\uD800x\"")]
    [InlineData("{\"\\uD800\":1}")]
    public void Strings_EscapedLoneSurrogates_Reject(string json)
    {
        Exception? error = Record.Exception(() => Canonical(json));
        Assert.True(error is JsonException or InvalidOperationException);
    }

    [Fact]
    public void Strings_TypedLoneSurrogates_CannotBeSilentlyReplaced()
    {
        PlanDefinition definition = PlanTestData.Definition();
        Assert.Throws<JsonException>(() => PlanCanonical.Serialize(definition with { Project = definition.Project with { Name = "\uD800" } }));
        Assert.Throws<JsonException>(() => PlanCanonical.Serialize(definition with { Project = definition.Project with { Name = "\uDC00" } }));
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("sessionFingerprint")]
    [InlineData("session_fingerprint")]
    [InlineData("ToStringIdentifier")]
    [InlineData("databaseIdentifier")]
    [InlineData("DATABASE_IDENTIFIER")]
    public void RuntimeIdentifiers_InNestedPayloads_CannotEnterDefinitions(string key)
    {
        PlanDefinition definition = PlanTestData.Definition();
        JsonElement forbidden = PlanTestData.Json("{\"nested\":[{\"" + key + "\":\"runtime\"}]}");
        PlanDefinition changed = definition with { Steps = [definition.Steps[0] with { Args = forbidden }] };
        Assert.Throws<JsonException>(() => PlanCanonical.Serialize(changed));
        Assert.Throws<JsonException>(() => PlanCanonical.ReadDefinition(JsonSerializer.Serialize(changed, PlanJson.Options)));
        Assert.Throws<JsonException>(() => PlanCanonical.Serialize(definition with { Inputs = new([new("page.update", forbidden)]) }));
        Assert.Throws<JsonException>(() => PlanCanonical.Serialize(definition with { Steps = [definition.Steps[0] with { Prediction = forbidden }] }));
    }

    private static string Canonical(string json) => PlanCanonical.Serialize(PlanTestData.Json(json));
}

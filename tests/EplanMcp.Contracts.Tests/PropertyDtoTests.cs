using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class PropertyDtoTests
{
    private static readonly JsonSerializerOptions _host = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerSettings _bridge = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateParseHandling = DateParseHandling.None,
    };

    private static ObjectLocator Locator() => new()
    {
        Project = "Line1",
        Kind = "function",
        Type = 17,
        Oid = 42,
        Name = "=AP+ST1-K1",
        Page = "=AP+ST1/3",
        Category = "Terminal",
        Pos = "100,160",
    };

    private static PropertyDefinitionMetadata Definition() => new()
    {
        ValueType = "integer",
        IsReadOnly = false,
        IsComputed = false,
        IsNamePart = false,
        IsInternal = false,
        IsPropertyLicensed = true,
        MaximumLength = 30,
        LowerBound = new("integer", integerValue: long.MinValue),
        UpperBound = new("integer", integerValue: long.MaxValue),
        EnumValues = [new("enum", integerValue: 1), new("enum", integerValue: 2)],
        Format = "verified-format",
        EmptyAllowed = false,
        IsIndexed = true,
        MinimumIndex = 2,
        MaximumIndex = 7,
        Unit = "mm",
        LanguageMode = "none",
    };

    // Synthetic evidence exercises the data model; it is never a published authorization entry.
    private static PropertyWriteEntry Entry() => new()
    {
        EplanMajorVersion = 2026,
        ObjectType = "Function",
        PropertyId = 20011,
        ApplicableCategory = "Terminal",
        PropertyName = "FUNC_TEXT",
        Definition = new()
        {
            ValueType = "multi_language",
            MaximumLength = 500,
            EmptyAllowed = true,
            IsIndexed = false,
            LanguageMode = "explicit_merge_preserve",
            Format = "literal",
        },
        Environments = [new() { EplanVersion = "2026.0.3", Variant = "Electric P8", LicenceOptions = ["test-option"] }],
        SetterMode = "test_setter",
        ReadBackMode = "test_readback",
        Dependencies = [new(propertyId: 20027)],
        SideEffectGuards = ["test_names_and_related_objects"],
        NormalizedComparison = "test_exact_all_languages",
        TestEvidence = ["synthetic:write/read/persist/undo/rollback/side-effects"],
        ApiNotesEvidence = ["synthetic:api-notes"],
    };

    public static TheoryData<object> Samples() =>
    [
        new PropertySelector(propertyId: 20011),
        new PropertySelector(propertyName: "FUNC_TEXT", index: 3),
        new PropertyTarget(projectObject: true),
        new PropertyTarget(page: "=AP+ST1/3"),
        new PropertyTarget(deviceTag: "=AP+ST1-K1"),
        new PropertyTarget(deviceTag: "=AP+ST1-K1", page: "=AP+ST1/3"),
        new PropertyTarget(locator: Locator()),
        new PropertyTypedValue("text", text: "literal;@text"),
        new PropertyTypedValue("text", text: ""),
        new PropertyTypedValue("boolean", boolean: true),
        new PropertyTypedValue("boolean", boolean: false),
        new PropertyTypedValue("integer", integerValue: long.MinValue),
        new PropertyTypedValue("integer", integerValue: long.MaxValue),
        new PropertyTypedValue("number", number: 0),
        new PropertyTypedValue("number", number: -1.5),
        new PropertyTypedValue("number", number: double.MaxValue),
        new PropertyTypedValue("datetime", dateTime: DateTimeOffset.Parse("2026-10-25T13:14:15.1234567+08:00", CultureInfo.InvariantCulture)),
        new PropertyTypedValue("enum", text: "PublishedName"),
        new PropertyTypedValue("enum", integerValue: 2),
        new PropertyTypedValue("multi_language", text: "literal@text;"),
        Definition(),
        new PropertyDefinitionMetadata(),
        Entry(),
        Entry().Environments[0],
        new PropertySchemaItem { PropertyId = 20011, PropertyName = "FUNC_TEXT", ApiWritable = true, WriteAllowed = false },
        new PropertySchemaItem { PropertyId = 20011, PropertyName = "FUNC_TEXT" },
        new PropertySchemaItem
        {
            PropertyId = 20011,
            PropertyName = "FUNC_TEXT",
            ReadAllowed = false,
            ReadReason = "pending classification",
            ApiWritable = false,
            WriteAllowed = false,
            WriteReason = "not published",
            Definition = Definition(),
            Source = "synthetic",
            Version = "2026.0.3",
            RuntimeDifferences = ["synthetic difference"],
        },
        new PropertyReadRow("empty") { Target = new(projectObject: true), PropertyId = 10013 },
        new PropertyReadRow("not_applicable") { Query = 1, Target = new(locator: Locator()), PropertyId = 20011, Reason = "not applicable" },
        new PropertyReadRow("read_refused") { Target = new(projectObject: true), PropertyId = 2000, Reason = "policy" },
        new PropertyReadRow("value") { Target = new(page: "=AP+ST1/3"), PropertyId = 11016, ValueType = "opaque", Display = "safe view" },
        new PropertyReadRow("value", new("multi_language", text: "selected@text;"))
        {
            Query = 2,
            Target = new(locator: Locator()),
            PropertyId = 20011,
            Index = 3,
            Language = "en_US",
            Fallback = true,
            ValueType = "multi_language",
            ReadOnly = false,
            Computed = false,
            ObservedAt = DateTimeOffset.Parse("2026-10-25T01:02:03+00:00", CultureInfo.InvariantCulture),
            Display = "selected@text;",
            Texts = [new() { Language = "en_US", Text = "selected@text;" }, new() { Language = "??_??", Text = "neutral" }],
            Unit = "mm",
            Format = "literal",
            Source = "synthetic",
            OriginalLength = 14,
        },
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public void PipeDtos_BothSerializers_RoundTripInBothDirections(object sample)
    {
        Type type = sample.GetType();
        string hostJson = System.Text.Json.JsonSerializer.Serialize(sample, type, _host);
        string bridgeJson = JsonConvert.SerializeObject(sample, _bridge);
        object bridgeCopy = JsonConvert.DeserializeObject(hostJson, type, _bridge)!;
        object hostCopy = System.Text.Json.JsonSerializer.Deserialize(bridgeJson, type, _host)!;

        AssertJsonEqual(hostJson, bridgeJson);
        AssertJsonEqual(hostJson, JsonConvert.SerializeObject(bridgeCopy, _bridge));
        AssertJsonEqual(hostJson, System.Text.Json.JsonSerializer.Serialize(hostCopy, type, _host));
    }

    [Fact]
    public void NumericPayloads_RoundTrip_RemainJsonNumbersWithExactIntegers()
    {
        using JsonDocument document = JsonDocument.Parse(JsonConvert.SerializeObject(new PropertyTypedValue("integer", integerValue: long.MaxValue), _bridge));
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("integerValue").ValueKind);
        Assert.Equal(long.MaxValue, document.RootElement.GetProperty("integerValue").GetInt64());
        Assert.False(document.RootElement.TryGetProperty("value_type", out _));
    }

    [Fact]
    public void Schema_UnknownFacts_RemainNullAndApiWritabilityGrantsNothing()
    {
        var unknown = new PropertySchemaItem();
        Assert.Null(unknown.ApiWritable);
        Assert.Null(unknown.WriteAllowed);
        Assert.Null(unknown.Definition.ValueType);
        Assert.Null(unknown.Definition.EmptyAllowed);
        Assert.Null(unknown.Definition.MinimumIndex);
        using JsonDocument document = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(unknown, _host));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("apiWritable").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("writeAllowed").ValueKind);
        var apiOnly = new PropertySchemaItem { ApiWritable = true };
        Assert.Null(apiOnly.WriteAllowed);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"propertyId\":20011,\"propertyName\":\"FUNC_TEXT\"}")]
    public void Selector_InvalidUnion_BothSerializersReject(string json) => AssertBothReject<PropertySelector>(json);

    public static TheoryData<string> InvalidTargets()
    {
        var cases = new TheoryData<string> { "{}", "{\"projectObject\":false}", "{\"owner\":{},\"articleReferenceIndex\":1}" };
        string[] branches = ["\"projectObject\":true", "\"page\":\"P\"", "\"deviceTag\":\"K1\"", "\"locator\":{}"];
        for (int mask = 1; mask < 16; mask++)
        {
            if ((mask & (mask - 1)) == 0 || mask == 6)
            {
                continue;
            }

            cases.Add("{" + string.Join(",", branches.Where((_, i) => (mask & (1 << i)) != 0)) + "}");
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(InvalidTargets))]
    public void Target_EveryConflictingBranch_BothSerializersReject(string json) => AssertBothReject<PropertyTarget>(json);

    public static TheoryData<string> InvalidValues()
    {
        var cases = new TheoryData<string>
        {
            "{}",
            "{\"valueType\":\"opaque\",\"text\":\"unsafe write\"}",
            "{\"valueType\":\"reset\",\"text\":\"\"}",
            "{\"valueType\":\"TEXT\",\"text\":\"x\"}",
            "{\"valueType\":\"number\",\"number\":\"NaN\"}",
            "{\"valueType\":\"number\",\"number\":\"Infinity\"}",
            "{\"valueType\":\"number\",\"number\":\"-Infinity\"}",
            "{\"valueType\":\"number\",\"number\":1e400}",
            "{\"valueType\":\"enum\",\"text\":\"Name\",\"integerValue\":1}",
        };
        string[] types = ["text", "boolean", "integer", "number", "datetime", "enum", "multi_language"];
        string[] payloads = ["\"text\":\"x\"", "\"boolean\":true", "\"integerValue\":1", "\"number\":1.5", "\"dateTime\":\"2026-10-25T00:00:00+00:00\""];
        foreach (string type in types)
        {
            cases.Add("{\"valueType\":\"" + type + "\"}");
            cases.Add("{\"valueType\":\"" + type + "\",\"text\":null}");
            for (int slot = 0; slot < payloads.Length; slot++)
            {
                bool matches = type switch
                {
                    "text" or "multi_language" => slot == 0,
                    "boolean" => slot == 1,
                    "integer" => slot == 2,
                    "number" => slot == 3,
                    "datetime" => slot == 4,
                    "enum" => slot is 0 or 2,
                    _ => false,
                };
                if (!matches)
                {
                    cases.Add("{\"valueType\":\"" + type + "\"," + payloads[slot] + "}");
                }
            }
        }

        for (int first = 0; first < payloads.Length; first++)
        {
            for (int second = first + 1; second < payloads.Length; second++)
            {
                cases.Add("{\"valueType\":\"text\"," + payloads[first] + "," + payloads[second] + "}");
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void Value_UnknownMissingMismatchedOrMultiplePayloads_BothSerializersReject(string json) => AssertBothReject<PropertyTypedValue>(json);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Value_NonFiniteNumber_ConstructorRejects(double number) =>
        Assert.Throws<ArgumentException>(() => new PropertyTypedValue("number", number: number));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"unknown\"}")]
    [InlineData("{\"status\":\"empty\",\"value\":{\"valueType\":\"text\",\"text\":\"leak\"}}")]
    [InlineData("{\"status\":\"not_applicable\",\"value\":{\"valueType\":\"text\",\"text\":\"leak\"}}")]
    [InlineData("{\"status\":\"read_refused\",\"value\":{\"valueType\":\"text\",\"text\":\"leak\"}}")]
    public void ReadRow_UnknownStatusOrNonValuePayload_BothSerializersReject(string json) => AssertBothReject<PropertyReadRow>(json);

    private static void AssertBothReject<T>(string json)
    {
        Exception? hostError = Record.Exception(() => System.Text.Json.JsonSerializer.Deserialize<T>(json, _host));
        Exception? bridgeError = Record.Exception(() => JsonConvert.DeserializeObject<T>(json, _bridge));
        Assert.True(hostError is ArgumentException or System.Text.Json.JsonException, hostError?.ToString() ?? "Host accepted an invalid union.");
        Assert.True(bridgeError is ArgumentException or Newtonsoft.Json.JsonException, bridgeError?.ToString() ?? "Bridge accepted an invalid union.");
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        using JsonDocument left = JsonDocument.Parse(expected);
        using JsonDocument right = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(left.RootElement, right.RootElement), actual);
    }
}

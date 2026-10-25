using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PropertyGetRowsTests
{
    [Theory]
    [InlineData("text", "\"literal\"")]
    [InlineData("boolean", "false")]
    [InlineData("integer", "9223372036854775807")]
    [InlineData("number", "1.5")]
    [InlineData("enum", "\"Member\"")]
    [InlineData("multi_language", "\"selected\"")]
    [InlineData("datetime", "\"2026-10-25T12:00:00+00:00\"")]
    public void TypedValues_MapToJsonPrimitivesWithoutCoercion(string type, string expected)
    {
        PropertyTypedValue value = type switch
        {
            "text" => new(type, text: "literal"),
            "boolean" => new(type, boolean: false),
            "integer" => new(type, integerValue: long.MaxValue),
            "number" => new(type, number: 1.5),
            "enum" => new(type, text: "Member"),
            "multi_language" => new(type, text: "selected"),
            _ => new(type, dateTime: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)),
        };
        PropertyReadRow row = Row("value", value);
        JsonObject mapped = PropertyGetRows.Map(row, 4, detailed: false);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), mapped["value"]));
        Assert.Equal(4, mapped["query"]!.GetValue<int>());
        Assert.Equal(20011, mapped["property_id"]!.GetValue<int>());
        Assert.Equal("K1", mapped["target"]!["device_tag"]!.GetValue<string>());
        Assert.False(mapped.ContainsKey("definition"));
        Assert.False(mapped.ContainsKey("value_type"));
        Assert.False(mapped.ContainsKey("display"));
    }

    [Fact]
    public void NumericEnum_StaysAnInteger()
    {
        JsonObject row = PropertyGetRows.Map(Row("value", new PropertyTypedValue("enum", integerValue: -2)), 0, true);
        Assert.Equal(-2, row["value"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("not_applicable")]
    [InlineData("read_refused")]
    public void NonValueStatuses_KeepRequestedAddressIndexAndReason(string status)
    {
        PropertyReadRow row = Row(status);
        row.Reason = status == "read_refused" ? "internal" : "no_stored_value";
        row.Index = 3;
        JsonObject result = PropertyGetRows.Map(row, 2, true);
        Assert.Equal(status, result["status"]!.GetValue<string>());
        Assert.Equal(row.Reason, result["reason"]!.GetValue<string>());
        Assert.Equal(3, result["index"]!.GetValue<int>());
        Assert.Null(result["value"]);
        Assert.Null(result["value_type"]);
        Assert.Equal(2, result["query"]!.GetValue<int>());
        Assert.Equal("K1", result["target"]!["device_tag"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Opaque20027_AlwaysKeepsDisplayAndNeverInventsATypedValue(bool detailed)
    {
        PropertyReadRow row = Row("value");
        row.PropertyId = 20027;
        row.ValueType = "opaque";
        row.Display = "1,5";
        JsonObject result = PropertyGetRows.Map(row, 0, detailed);
        Assert.Equal("value", result["status"]!.GetValue<string>());
        Assert.Null(result["value"]);
        Assert.Equal("1,5", result["display"]!.GetValue<string>());
        Assert.False(result.ContainsKey("definition"));
    }

    [Fact]
    public void Detailed_PrecleanedStoredTranslationsAndUnknownFacts_ArePreserved()
    {
        PropertyReadRow row = Row("value", new PropertyTypedValue("multi_language", text: "selected"));
        row.Language = "de_DE";
        row.Fallback = true;
        row.ReadOnly = true;
        row.Texts =
        [
            new SnapshotText
            {
                Language = "??_??",
                Text = "independent",
            },
            new SnapshotText
            {
                Language = "de_DE",
                Text = "stored\n\t",
            },
        ];
        row.Display = "display";
        row.Unit = "mm";
        row.Format = "format";
        row.Source = "runtime";
        row.Definition = new PropertyDefinitionMetadata();
        JsonObject detailed = PropertyGetRows.Map(row, 0, true);
        Assert.Equal("selected", detailed["value"]!.GetValue<string>());
        Assert.Equal("de_DE", detailed["language"]!.GetValue<string>());
        Assert.True(detailed["fallback"]!.GetValue<bool>());
        Assert.True(detailed["read_only"]!.GetValue<bool>());
        Assert.Null(detailed["computed"]);
        Assert.Null(detailed["observed_at"]);
        Assert.Equal("independent", detailed["texts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("??_??", detailed["texts"]![0]!["language"]!.GetValue<string>());
        Assert.Equal("stored\n\t", detailed["texts"]![1]!["text"]!.GetValue<string>());
        Assert.Equal("display", detailed["display"]!.GetValue<string>());
        Assert.Equal("mm", detailed["unit"]!.GetValue<string>());
        Assert.Equal("format", detailed["format"]!.GetValue<string>());
        Assert.Equal("runtime", detailed["source"]!.GetValue<string>());
        JsonObject concise = PropertyGetRows.Map(row, 0, false);
        Assert.True(concise["fallback"]!.GetValue<bool>());
        Assert.False(concise.ContainsKey("texts"));
    }

    [Fact]
    public void BridgeRefusal_DropsAllValueSideChannelsAndKeepsTheOriginalReason()
    {
        PropertyReadRow row = Row("read_refused");
        row.Reason = "unknown_internal";
        row.ValueType = "opaque";
        row.Display = "C:\\private\\data";
        row.Texts =
        [
            new SnapshotText
            {
                Language = "en_US",
                Text = "person@example.test",
            },
        ];
        row.Language = "en_US";
        row.Fallback = true;
        row.Definition = new PropertyDefinitionMetadata
        {
            Unit = "C:\\private\\data",
        };
        row.ReadOnly = true;
        row.Computed = true;
        row.Unit = "mm";
        row.Source = "runtime";
        row.Format = "format";
        row.OriginalLength = 600;
        JsonObject result = PropertyGetRows.Map(row, 1, true);
        Assert.Equal("read_refused", result["status"]!.GetValue<string>());
        Assert.Equal("unknown_internal", result["reason"]!.GetValue<string>());
        string[] facts = ["value", "value_type", "display", "texts", "language", "fallback", "unit", "format", "source", "read_only", "computed", "original_length"];
        Assert.All(facts, key => Assert.Null(result[key]));
        Assert.DoesNotContain("private", result.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", result.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeTextTruncation_IsNotAppliedAgainAndKeepsItsOriginalLength()
    {
        string raw = new('x', 700);
        string cleaned = ResultShaper.CleanText(raw);
        PropertyReadRow alreadyCleaned = Row("value", new PropertyTypedValue("text", text: cleaned));
        alreadyCleaned.OriginalLength = 700;
        JsonObject second = PropertyGetRows.Map(alreadyCleaned, 0, true);
        Assert.Equal(cleaned, second["value"]!.GetValue<string>());
        Assert.Equal(700, second["original_length"]!.GetValue<int>());
        Assert.Equal(cleaned, alreadyCleaned.Value!.Text);
    }

    [Fact]
    public void RefusalReason_UsesTheExistingHostCleanTextRule()
    {
        PropertyReadRow row = Row("read_refused");
        row.Reason = "unknown\u202e_internal\u0001";
        Assert.Equal("unknown_internal", PropertyGetRows.Map(row, 0, false)["reason"]!.GetValue<string>());
    }

    [Fact]
    public void LocatorAddress_PreservesProjectIdentityAndCleansItsCheckTexts()
    {
        PropertyReadRow row = Row("empty");
        row.Target = new PropertyTarget(locator: new ObjectLocator
        {
            Project = PropertyGetRequestTests.Project,
            Kind = "connection",
            Type = 21,
            Oid = 42,
            Name = "N\u202e",
            Page = "P\u0001",
        });
        JsonNode locator = PropertyGetRows.Map(row, 3, true)["target"]!["locator"]!;
        Assert.Equal(PropertyGetRequestTests.Project, locator["project"]!.GetValue<string>());
        Assert.Equal("N", locator["name"]!.GetValue<string>());
        Assert.Equal("P", locator["page"]!.GetValue<string>());
        Assert.Equal(42, locator["oid"]!.GetValue<int>());
    }

    internal static PropertyReadRow Row(string status, PropertyTypedValue? value = null) => new(status, value)
    {
        Target = new PropertyTarget(deviceTag: "K1"),
        PropertyId = 20011,
        ValueType = value?.ValueType,
    };
}

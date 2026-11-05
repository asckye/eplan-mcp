using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PropertySetContractTests
{
    [Fact]
    public void Schemas_UseClosedSharedTargetAndSelectorDefinitionsAndPublishedLimits()
    {
        JsonElement input = PropertySetContract.Input;
        JsonElement fields = input.GetProperty("properties");
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["project", "changes"], input.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.True(JsonElement.DeepEquals(PropertyGetContract.Input.GetProperty("$defs"), input.GetProperty("$defs")));
        Assert.Equal(1, fields.GetProperty("changes").GetProperty("minItems").GetInt32());
        Assert.Equal(100, fields.GetProperty("changes").GetProperty("maxItems").GetInt32());
        Assert.True(fields.GetProperty("dry_run").GetProperty("default").GetBoolean());
        Assert.Equal("concise", fields.GetProperty("response_format").GetProperty("default").GetString());
        Assert.Equal(["concise", "detailed"], fields.GetProperty("response_format").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    [Theory]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20011},\"language\":\"zh_CN\",\"value\":\"literal; @\"}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_name\":\"FUNC_TECHNICAL_CHARACTERISTIC\"},\"value\":\"\"}")]
    public void PublicStrings_ConvertToExactBridgeTypedValues(string change)
    {
        Dictionary<string, JsonElement> input = Input("{\"project\":\"Line1\",\"changes\":[" + change + "]}");
        PropertySetChange parsed = Assert.Single(PropertySetRequest.Parse(input).Changes);
        Assert.Equal(parsed.Language is null ? "text" : "multi_language", parsed.Value.ValueType);
        Assert.True(PropertyGetContractTests.Accepts(PropertySetContract.Input, JsonSerializer.SerializeToElement(input), PropertySetContract.Input));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20011},\"value\":\"text\"}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20011},\"language\":\"zh_CN\",\"value\":true}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027},\"value\":27}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027},\"value\":null}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027,\"index\":1},\"value\":\"text\"}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027},\"language\":\"en_US\",\"value\":\"text\"}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\",\"extra\":true},\"property\":{\"property_id\":20027},\"value\":\"text\"}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027,\"property_name\":\"FUNC_TECHNICAL_CHARACTERISTIC\"},\"value\":\"text\"}")]
    [InlineData("{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027},\"value\":\"text\",\"extra\":true}")]
    public void InvalidAssignments_AreRefusedWithViolations(string change)
    {
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => PropertySetRequest.Parse(Input("{\"project\":\"Line1\",\"changes\":[" + change + "]}")));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.NotEmpty(error.Error.Metadata!["violations"]);
    }

    [Theory]
    [InlineData("\"dry_run\":null")]
    [InlineData("\"dry_run\":\"true\"")]
    [InlineData("\"response_format\":null")]
    [InlineData("\"response_format\":\"verbose\"")]
    [InlineData("\"extra\":true")]
    public void InvalidOptions_AreRefusedBeforeAnyBridgeCall(string option)
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => PropertySetRequest.Parse(Input(
            "{\"project\":\"Line1\",\"changes\":[{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027},\"value\":\"text\"}]," + option + "}"))).Error.Reason);
    }

    [Fact]
    public void ObjectCountAndTextBounds_AreCheckedBeforePlanning()
    {
        foreach (int count in new[] { 0, 51, 101 })
        {
            var input = new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement("Line1"),
                ["changes"] = JsonSerializer.SerializeToElement(Enumerable.Range(0, count).Select(i => new
                {
                    target = new { device_tag = "K" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    property = new { property_id = 20027 },
                    value = "text",
                })),
            };
            Assert.Throws<BridgeCallException>(() => PropertySetRequest.Parse(input));
        }
        Assert.Throws<BridgeCallException>(() => PropertySetRequest.Parse(Input(
            "{\"project\":\"Line1\",\"changes\":[{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20027},\"value\":\"" + new string('x', 501) + "\"}]}")));
    }

    [Theory]
    [InlineData(20161, "\"Line1\"", "text", null)]
    [InlineData(20408, "\"3\"", "text", null)]
    [InlineData(20308, "12", "integer", 12L)]
    [InlineData(20400, "\"%IX2.0\"", "text", null)]
    public void B8_PrimitivesPreserveTheirPublishedType(int id, string value, string type, long? number)
    {
        PropertySetChange parsed = Assert.Single(PropertySetRequest.Parse(Input(
            "{\"project\":\"Line1\",\"changes\":[{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":" + id + "},\"value\":" + value + "}]}")).Changes);
        Assert.Equal(type, parsed.Value.ValueType);
        Assert.Equal(number, parsed.Value.IntegerValue);
    }

    [Theory]
    [InlineData(20367, "\"1\"", "text", null)]
    [InlineData(20809, "\"1\"", "text", null)]
    [InlineData(20035, "60", "integer", 60L)]
    [InlineData(20035, "-2147483648", "integer", -2147483648L)]
    [InlineData(20035, "2147483647", "integer", 2147483647L)]
    public void TerminalPrimitives_PreservePublishedTypes(int id, string value, string type, long? number)
    {
        PropertySetChange parsed = Assert.Single(PropertySetRequest.Parse(Input(
            "{\"project\":\"Line1\",\"changes\":[{\"target\":{\"device_tag\":\"+A1-XQ10:1\"},\"property\":{\"property_id\":" + id + "},\"value\":" + value + "}]}")).Changes);
        Assert.Equal(type, parsed.Value.ValueType);
        Assert.Equal(number, parsed.Value.IntegerValue);
    }

    [Theory]
    [InlineData("\"60\"")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1.5")]
    [InlineData("true")]
    public void TerminalInteger_RejectsCoercionAndOverflow(string value)
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => PropertySetRequest.Parse(Input(
            "{\"project\":\"Line1\",\"changes\":[{\"target\":{\"device_tag\":\"+A1-XQ10:1\"},\"property\":{\"property_id\":20035},\"value\":" + value + "}]}"))).Error.Reason);
    }

    [Theory]
    [InlineData("\"12\"")]
    [InlineData("-1")]
    [InlineData("52")]
    [InlineData("12.5")]
    [InlineData("true")]
    public void B8_BusSystemRejectsCoercionAndUndocumentedMembers(string value)
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => PropertySetRequest.Parse(Input(
            "{\"project\":\"Line1\",\"changes\":[{\"target\":{\"device_tag\":\"K1\"},\"property\":{\"property_id\":20308},\"value\":" + value + "}]}"))).Error.Reason);
    }

    internal static bool AcceptsOutput(JsonElement data) => PropertyGetContractTests.Accepts(PropertySetContract.Output, data, PropertySetContract.Output);
    private static Dictionary<string, JsonElement> Input(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}

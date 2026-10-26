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
        Assert.False(fields.GetProperty("dry_run").GetProperty("default").GetBoolean());
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

    internal static bool AcceptsOutput(JsonElement data) => PropertyGetContractTests.Accepts(PropertySetContract.Output, data, PropertySetContract.Output);
    private static Dictionary<string, JsonElement> Input(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}

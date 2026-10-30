using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class AcceptanceDrawingListTests
{
    internal static string Fixture()
    {
        using Stream stream = typeof(AcceptanceDrawingListTests).Assembly.GetManifestResourceStream("Acceptance.fictional.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void FictionalList_ContainsTheRequestedIndependentEngineeringInputs()
    {
        JsonElement list = AcceptanceDrawingList.Read(Fixture());
        Assert.Equal(6, list.GetProperty("devices").GetArrayLength());
        Assert.Equal(5, list.GetProperty("devices").EnumerateArray().Count(d => d.GetProperty("tag").GetString()!.Contains("+P1-", StringComparison.Ordinal)));
        Assert.Single(list.GetProperty("devices").EnumerateArray(), d => d.GetProperty("tag").GetString()!.Contains("+HMT-", StringComparison.Ordinal));
        Assert.Equal(["RD", "WH", "BN", "GY", "BU", "VT"], list.GetProperty("cables")[0].GetProperty("cores").EnumerateArray().Select(c => c.GetProperty("colour").GetString()));
        Assert.Single(list.GetProperty("cables")[1].GetProperty("cores").EnumerateArray());
        Assert.Equal(Enumerable.Range(0, 7).Select(i => "I0." + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), list.GetProperty("plc").GetProperty("points").EnumerateArray().Select(p => p.GetProperty("address").GetString()));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), list.GetProperty("terminal_strip").GetProperty("terminals").EnumerateArray().Select(t => t.GetProperty("number").GetString()));
        Assert.Equal(5, list.GetProperty("interruption_points").GetArrayLength());
        Assert.Contains(list.GetProperty("nodes").EnumerateArray(), n => n.GetProperty("kind").GetString() == "t_node");
    }

    [Theory]
    [InlineData("version", "2", "/version")]
    [InlineData("page", "null", "/page")]
    [InlineData("devices", "{}", "/devices")]
    [InlineData("a/b~c", "true", "/a~1b~0c")]
    public void InvalidMember_IsRejectedWithPointer(string key, string value, string path)
    {
        JsonObject list = JsonNode.Parse(Fixture())!.AsObject();
        list[key] = JsonNode.Parse(value);
        Assert.Contains(path, Assert.Throws<JsonException>(() => AcceptanceDrawingList.Read(list.ToJsonString())).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"version\": 1", "\"version\": 1, \"version\": 1", "/version")]
    [InlineData("\"cross_section_mm2\": 0.5", "\"cross_section_mm2\": 1e999", "/cables/0/cross_section_mm2")]
    [InlineData("\"address\": \"I0.1\"", "\"address\": \"I0.0\"", "/plc/points/1/address")]
    [InlineData("\"number\": \"2\"", "\"number\": \"1\"", "/terminal_strip/terminals/1/number")]
    [InlineData("\"colour\": \"RD\"", "\"colour\": \"RED\"", "/cables/0/cores/0/colour")]
    [InlineData("\"wire\": \"signal2\"", "\"wire\": \"signal1\"", "/cables/0/cores/1/wire")]
    public void InvalidEngineeringValue_IsRejectedBeforeAnyToolCall(string before, string after, string path)
    {
        string json = Fixture().Replace(before, after, StringComparison.Ordinal);
        Assert.NotEqual(Fixture(), json);
        Assert.Contains(path, Assert.Throws<JsonException>(() => AcceptanceDrawingList.Read(json)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WirePath_UnknownPinDiagonalAndMisplacedAnchor_AreRejected()
    {
        foreach ((string field, string value, string pointer) in new[]
        {
            ("from", "{\"item\":\"switch1\",\"pin\":\"99\"}", "/wires/0/from"),
            ("points", "[[0,0],[4,8]]", "/wires/0/points/1"),
            ("points", "[\"from\",\"from\",\"to\"]", "/wires/0/points/1"),
        })
        {
            JsonObject list = JsonNode.Parse(Fixture())!.AsObject();
            list["wires"]![0]![field] = JsonNode.Parse(value);
            Assert.Contains(pointer, Assert.Throws<JsonException>(() => AcceptanceDrawingList.Read(list.ToJsonString())).Message, StringComparison.Ordinal);
        }
    }
}

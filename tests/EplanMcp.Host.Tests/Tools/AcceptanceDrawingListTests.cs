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
        Assert.All(list.GetProperty("plc").GetProperty("points").EnumerateArray(), point =>
        {
            Assert.Equal("Up", point.GetProperty("direction").GetString());
            Assert.Equal([20400, 20403, 20011, 20301], point.GetProperty("display_properties").EnumerateArray().Select(p => p.GetProperty("property_id").GetInt32()));
        });
        Assert.Contains(list.GetProperty("nodes").EnumerateArray(), n => n.GetProperty("kind").GetString() == "t_node");
        JsonElement support = list.GetProperty("interruption_support");
        Assert.All(support.EnumerateObject(), p =>
        {
            Assert.Equal("O_EL", p.Value.GetProperty("device").GetProperty("symbol").GetString());
            Assert.Equal("Down", Assert.Single(p.Value.GetProperty("potential_point").GetProperty("directions").EnumerateArray()).GetString());
        });
    }

    [Fact]
    public void PairedInterruptions_RequireExplicitSupportOnBothPages_AndUniqueNames()
    {
        foreach ((string pointer, Action<JsonObject> change) in new (string, Action<JsonObject>)[]
        {
            ("/interruption_support", l => l.Remove("interruption_support")),
            ("/interruption_support/counterpart_page", l => l["interruption_support"]!.AsObject().Remove("counterpart_page")),
            ("/interruption_support/page/potential_point/directions", l => l["interruption_support"]!["page"]!["potential_point"]!["directions"] = new JsonArray("Diagonal")),
            ("/interruption_points/1/name", l => l["interruption_points"]![1]!["name"] = l["interruption_points"]![0]!["name"]!.DeepClone()),
        })
        {
            JsonObject list = JsonNode.Parse(Fixture())!.AsObject();
            change(list);
            Assert.Contains(pointer, Assert.Throws<JsonException>(() => AcceptanceDrawingList.Read(list.ToJsonString())).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CombinedWireNodeAndCableLimit_IsEnforcedBeforeCallingTools()
    {
        JsonObject list = JsonNode.Parse(Fixture())!.AsObject();
        JsonArray nodes = list["nodes"]!.AsArray();
        for (int i = 0; i < 29; i++)
        {
            JsonObject node = nodes[0]!.DeepClone().AsObject();
            node["id"] = "extra" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            nodes.Add(node);
        }
        Assert.Equal(49, list["wires"]!.AsArray().Count + nodes.Count);
        Assert.Contains("/cables", Assert.Throws<JsonException>(() => AcceptanceDrawingList.Read(list.ToJsonString())).Message, StringComparison.Ordinal);
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
    public void NativePointInputs_ContradictoryNamesDirectionsAndDisplays_AreRejected()
    {
        foreach ((string field, string value) in new[] { ("direction", "\"Diagonal\""), ("tag", "\"-OTHER:1\""), ("connection_designations", "[\"wrong\"]"), ("display_properties", "[{\"property_id\":999,\"offset_x_mm\":0,\"offset_y_mm\":0}]") })
        {
            JsonObject list = JsonNode.Parse(Fixture())!.AsObject();
            list["plc"]!["points"]![0]![field] = JsonNode.Parse(value);
            Assert.Contains("/plc/points/0/" + field, Assert.Throws<JsonException>(() => AcceptanceDrawingList.Read(list.ToJsonString())).Message, StringComparison.Ordinal);
        }
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

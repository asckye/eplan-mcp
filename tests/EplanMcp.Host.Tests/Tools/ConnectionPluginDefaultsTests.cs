using System.Text.Json;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ConnectionPluginDefaultsTests
{
    [Fact]
    public void OmittedSymbol_UsesWinningPluginAndDoesNotMutateInput()
    {
        JsonElement[] input = Items("""[{"kind":"interruption_point","position":[4,8]},{"kind":"corner"}]""");
        (JsonElement[] items, string[] plugins) = ConnectionPluginDefaults.Apply(input, Resolution());
        Assert.Equal("BPIN", items[0].GetProperty("symbol").GetString());
        Assert.Equal(["standard@2.0.0"], plugins);
        Assert.False(input[0].TryGetProperty("symbol", out _));
        Assert.Equal(input[1].GetRawText(), items[1].GetRawText());
    }

    [Theory]
    [InlineData("\"BPOUT\"")]
    [InlineData("null")]
    public void ExplicitSymbol_IsAlwaysPreserved(string symbol)
    {
        JsonElement[] input = Items("[{\"kind\":\"interruption_point\",\"symbol\":" + symbol + "}]");
        (JsonElement[] items, string[] plugins) = ConnectionPluginDefaults.Apply(input, Resolution());
        Assert.Equal(input[0].GetRawText(), items[0].GetRawText());
        Assert.Empty(plugins);
    }

    [Fact]
    public void NoPluginDefault_PreservesBuiltInFallbackAndOmitsMetadataSources()
    {
        JsonElement[] input = Items("""[{"kind":"interruption_point"}]""");
        (JsonElement[] items, string[] plugins) = ConnectionPluginDefaults.Apply(input, new(Element("[]"), [], [], Element("[]")));
        Assert.Equal(input[0].GetRawText(), items[0].GetRawText());
        Assert.Empty(plugins);
    }

    [Fact]
    public void BindingWinner_ReportsOnlyItsPack()
    {
        PluginResolution resolution = new(Element("""[{"key":"defaults.interruption.symbol","value":"BPIN","winner":{"source":{"resource":"bound@1.0.0:config_layer/defaults"}},"overridden":[{"plugin":"standard@2.0.0"}]}]"""), [], [], Element("[]"));
        Assert.Equal(["bound@1.0.0"], ConnectionPluginDefaults.Apply(Items("""[{"kind":"interruption_point"}]"""), resolution).Plugins);
    }

    private static PluginResolution Resolution() => new(Element("""[{"key":"defaults.interruption.symbol","value":"BPIN","winner":{"plugin":"standard@2.0.0"},"overridden":[{"plugin":"older@1.0.0"}]}]"""), ["older@1.0.0", "standard@2.0.0"], [], Element("[]"));
    private static JsonElement Element(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static JsonElement[] Items(string json) => JsonSerializer.Deserialize<JsonElement[]>(json)!;
}

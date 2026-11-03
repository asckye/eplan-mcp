using System.Text.Json;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class DevicePluginDefaultsTests
{
    [Fact]
    public void ProjectOverrides_AreEffectiveWithoutClaimingAPluginWinner()
    {
        var resolution = new PluginResolution(JsonSerializer.Deserialize<JsonElement>("""
            [{"key":"defaults.strip_definition.symbol","value":"TDEF","winner":{"layer":"project","source":{"project":"test"}}},
             {"key":"defaults.strip_definition.variant","value":0,"winner":{"layer":"project","source":{"project":"test"}}}]
            """), [], [], JsonSerializer.Deserialize<JsonElement>("[]"));
        var selection = DevicePluginDefaults.Select(resolution);
        Assert.NotNull(selection);
        Assert.Equal("TDEF", selection.Symbol);
        Assert.Equal(0, selection.Variant);
        Assert.Empty(selection.Plugins);
    }

    [Fact]
    public void MissingVariant_KeepsTheBuiltinFallback()
    {
        var resolution = new PluginResolution(JsonSerializer.Deserialize<JsonElement>("""
            [{"key":"defaults.strip_definition.symbol","value":"TDEF","winner":{"plugin":"iec-base@1.0.0"}}]
            """), ["iec-base@1.0.0"], [], JsonSerializer.Deserialize<JsonElement>("[]"));
        Assert.Null(DevicePluginDefaults.Select(resolution));
    }
}

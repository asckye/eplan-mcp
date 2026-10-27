using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class DevicePlaceDtoTests
{
    [Fact]
    public void SymbolSource_RoundTripsAcrossBothPipeSerializers()
    {
        var args = new DevicePlaceArgs
        {
            Project = "Line1",
            Page = "=F+A1/1",
            Library = "IEC_symbol",
            Symbol = "K",
            SymbolVariant = 0,
            X = 80,
            Y = 160,
            Tag = "-K2",
            DisplayProperties = [new() { PropertyId = 20214, OffsetXmm = 4, OffsetYmm = -4 }],
            ConnectionDesignations = ["A1", "A2"],
        };
        string json = System.Text.Json.JsonSerializer.Serialize(args, JsonSerializerOptions.Web);
        DevicePlaceArgs bridge = JsonConvert.DeserializeObject<DevicePlaceArgs>(json)!;
        Assert.Equal(("IEC_symbol", "K", 0), (bridge.Library, bridge.Symbol, bridge.SymbolVariant));
        DeviceDisplayProperty display = Assert.Single(bridge.DisplayProperties!);
        Assert.Equal((20214, 4d, -4d), (display.PropertyId, display.OffsetXmm, display.OffsetYmm));
        DevicePlaceArgs host = System.Text.Json.JsonSerializer.Deserialize<DevicePlaceArgs>(JsonConvert.SerializeObject(bridge))!;
        Assert.Equal(["A1", "A2"], host.ConnectionDesignations!);
        Assert.Null(host.Part);
        Assert.Null(host.Variant);
    }
}

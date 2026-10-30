using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class DevicePlaceDtoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlcGet_DetailedAndIndependentLanguage_RoundTripAcrossPipeSerializers(bool detailed)
    {
        var args = new PlcGetArgs { Plc = "-KF1", Language = "??_??", Detailed = detailed };
        PlcGetArgs bridge = JsonConvert.DeserializeObject<PlcGetArgs>(System.Text.Json.JsonSerializer.Serialize(args, JsonSerializerOptions.Web))!;
        Assert.Equal(("-KF1", "??_??", detailed), (bridge.Plc, bridge.Language, bridge.Detailed));
        PlcGetArgs host = System.Text.Json.JsonSerializer.Deserialize<PlcGetArgs>(JsonConvert.SerializeObject(bridge))!;
        Assert.Equal(detailed, host.Detailed);
    }

    [Fact]
    public void Plc_RoundTripsInputsAndComputedReadbackAcrossPipeSerializers()
    {
        var args = new DevicePlaceArgs { Tag = "-KF1", PlcConnectionPoint = "DI a.0", PlcAddress = "I0.0", PlcSymbolicAddress = "Start", PlcText = "Start input" };
        DevicePlaceArgs bridge = JsonConvert.DeserializeObject<DevicePlaceArgs>(System.Text.Json.JsonSerializer.Serialize(args, JsonSerializerOptions.Web))!;
        Assert.Equal(("-KF1", "DI a.0", "I0.0", "Start", "Start input"), (bridge.Tag, bridge.PlcConnectionPoint, bridge.PlcAddress, bridge.PlcSymbolicAddress, bridge.PlcText));
        var data = new DevicePlaceData { Plc = new() { Name = "+A1-KF1:DI a.0", Owner = "+A1-KF1", CardReference = "/2.1", ManualSymbolicAddress = "Start", SymbolicAddress = "Start", ParentlessRepresentations = 1 } };
        DevicePlaceData host = System.Text.Json.JsonSerializer.Deserialize<DevicePlaceData>(JsonConvert.SerializeObject(data))!;
        Assert.Equal(("+A1-KF1", "/2.1", "Start", "Start", 1), (host.Plc!.Owner, host.Plc.CardReference, host.Plc.ManualSymbolicAddress, host.Plc.SymbolicAddress, host.Plc.ParentlessRepresentations));
        var symbol = new SymbolSearchItem { FunctionCategory = "PLCTerminal", PlcInputDefinition = "1/4" };
        SymbolSearchItem copy = JsonConvert.DeserializeObject<SymbolSearchItem>(System.Text.Json.JsonSerializer.Serialize(symbol, JsonSerializerOptions.Web))!;
        Assert.Equal(("PLCTerminal", "1/4"), (copy.FunctionCategory, copy.PlcInputDefinition));
    }

    [Fact]
    public void Terminal_RoundTripsAcrossBothPipeSerializers()
    {
        var args = new DevicePlaceArgs { TerminalStrip = "-X6", TerminalNumber = "1", TerminalStripText = "Control box", SymbolVariant = 2 };
        DevicePlaceArgs bridge = JsonConvert.DeserializeObject<DevicePlaceArgs>(System.Text.Json.JsonSerializer.Serialize(args, JsonSerializerOptions.Web))!;
        Assert.Equal(("-X6", "1", "Control box", 2), (bridge.TerminalStrip, bridge.TerminalNumber, bridge.TerminalStripText, bridge.SymbolVariant));
        var data = new DevicePlaceData { Terminal = new() { Strip = "=F+A1-X6", Owner = "=F+A1-X6", Number = "1", Position = [80, 160], SortCode = "17", StripCreated = true } };
        DevicePlaceData host = System.Text.Json.JsonSerializer.Deserialize<DevicePlaceData>(JsonConvert.SerializeObject(data))!;
        Assert.Equal(("=F+A1-X6", "1", "17", true), (host.Terminal!.Owner, host.Terminal.Number, host.Terminal.SortCode, host.Terminal.StripCreated));
        Assert.Equal([80d, 160d], host.Terminal.Position);
    }

    [Fact]
    public void LocationBox_RoundTripsAcrossBothPipeSerializers()
    {
        var args = new DevicePlaceArgs { Project = "Line1", Page = "/1", LocationBox = "+P1", X = 60, Y = 140, CornerX = 100, CornerY = 180 };
        DevicePlaceArgs bridge = JsonConvert.DeserializeObject<DevicePlaceArgs>(System.Text.Json.JsonSerializer.Serialize(args, JsonSerializerOptions.Web))!;
        Assert.Equal(("+P1", 100d, 180d), (bridge.LocationBox, bridge.CornerX, bridge.CornerY));
        Assert.Null(bridge.Tag);
        var data = new DevicePlaceData { LocationBox = new() { Structure = "+P1", Bounds = [60, 140, 100, 180] } };
        DevicePlaceData host = System.Text.Json.JsonSerializer.Deserialize<DevicePlaceData>(JsonConvert.SerializeObject(data))!;
        Assert.Equal("+P1", host.LocationBox!.Structure);
        Assert.Equal([60d, 140d, 100d, 180d], host.LocationBox.Bounds);
        Assert.Equal("dashed", host.LocationBox.Outline);
        Assert.Empty(host.LocationBox.AffectedDevices);
    }

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

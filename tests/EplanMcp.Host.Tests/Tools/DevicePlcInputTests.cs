using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class DevicePlcInputTests
{
    [Theory]
    [InlineData("??_??@Start;en_US@Start;")]
    [InlineData("en_US@Start;??_??@Start;")]
    [InlineData("en_US@Start;")]
    public void FunctionTexts_SerializedLanguageEntries_AreRejectedBeforeCallingTheBridge(string raw)
    {
        DevicePlaceArgs plc = Args();
        plc.PlcText = raw;
        var strip = new DevicePlaceArgs { Project = "Line1", TerminalStrip = "-X1", TerminalNumber = "1", TerminalStripText = raw };
        Assert.Contains(DevicePlaceInputs.Check(plc, null), p => p.StartsWith("/plc_text:", StringComparison.Ordinal));
        Assert.Contains(DevicePlaceInputs.Check(strip, null), p => p.StartsWith("/terminal_strip_text:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("X10:2")]
    [InlineData("DI a.0")]
    public void Plc_ValidDesignationAndNativeDisplayFields_AreAccepted(string point)
    {
        DevicePlaceArgs args = Args();
        args.PlcConnectionPoint = point;
        foreach (int id in new[] { 20400, 20403, 20011, 20301 })
        {
            Assert.Empty(DevicePlaceInputs.Check(args, [JsonSerializer.SerializeToElement(new { property_id = id, offset_x_mm = -4, offset_y_mm = 4 })]));
        }
    }

    [Theory]
    [InlineData("missingPoint")]
    [InlineData("missingAddress")]
    [InlineData("missingSymbol")]
    [InlineData("missingText")]
    [InlineData("blank")]
    [InlineData("control")]
    [InlineData("length")]
    [InlineData("colon")]
    [InlineData("pointLength")]
    [InlineData("part")]
    [InlineData("box")]
    [InlineData("terminal")]
    public void Plc_InvalidOrConflictingFields_AreRejected(string field)
    {
        DevicePlaceArgs args = Args();
        switch (field)
        {
            case "missingPoint":
                args.PlcConnectionPoint = null;
                break;
            case "missingAddress":
                args.PlcAddress = null;
                break;
            case "missingSymbol":
                args.PlcSymbolicAddress = null;
                break;
            case "missingText":
                args.PlcText = null;
                break;
            case "blank":
                args.PlcText = " ";
                break;
            case "control":
                args.PlcAddress = "I0.0\n";
                break;
            case "length":
                args.PlcText = new string('x', 257);
                break;
            case "pointLength":
                args.PlcConnectionPoint = new string('x', 65);
                break;
            case "colon":
                args.PlcConnectionPoint = ":2";
                break;
            case "part":
                args.Library = null;
                args.Part = "Part";
                break;
            case "box":
                args.LocationBox = "+P1";
                break;
            case "terminal":
                args.TerminalStrip = "-X1";
                break;
        }
        Assert.Contains(DevicePlaceInputs.Check(args, null), p => p.StartsWith("/plc_connection_point:", StringComparison.Ordinal));
    }

    [Fact]
    public void Plc_ComputedFieldsAreDisplayOnly_OffsetsRemainStrictAndFinite()
    {
        foreach (string raw in new[]
        {
            """{"property_id":20402,"offset_x_mm":0,"offset_y_mm":0}""",
            """{"property_id":20301,"offset_x_mm":1e300,"offset_y_mm":0}""",
            """{"property_id":20301,"offset_x_mm":0,"offset_y_mm":0,"extra":0}""",
            """{"property_id":20301,"offset_x_mm":0,"offset_x_mm":1,"offset_y_mm":0}"""
        })
        {
            DevicePlaceArgs args = Args();
            args.X = double.MaxValue;
            using JsonDocument json = JsonDocument.Parse(raw);
            Assert.Contains(DevicePlaceInputs.Check(args, [json.RootElement]), p => p.StartsWith("/display_properties/0:", StringComparison.Ordinal));
        }
    }

    private static DevicePlaceArgs Args() => new() { Project = "Line1", Tag = "-KF1", Library = "IEC_symbol", Symbol = "PLC_DI", SymbolVariant = 0, PlcConnectionPoint = "X10:2", PlcAddress = "I0.0", PlcSymbolicAddress = "Start", PlcText = "Start input" };
}

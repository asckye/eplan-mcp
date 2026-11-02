using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DevicePlcPlanTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-X10\n")]
    [InlineData(null)]
    public void Plug_InvalidOrMissingPlcSource_IsRejectedBeforeWriting(string? plug)
    {
        var args = new DevicePlaceArgs { PlcPlugDesignation = plug ?? "-X10" };
        Assert.Contains("/plc_plug_designation", Assert.Throws<BridgeErrorException>(() => DevicePlcPlan.Check(args)).Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(20400)]
    [InlineData(20403)]
    [InlineData(20011)]
    [InlineData(20301)]
    public void Check_RequestedPlcDisplay_IsAccepted(int propertyId)
    {
        var args = new DevicePlaceArgs
        {
            Library = "IEC_symbol",
            PlcConnectionPoint = "X10:2",
            PlcAddress = "I0.0",
            PlcSymbolicAddress = "Start",
            PlcText = "Start input",
            DisplayProperties = [new() { PropertyId = propertyId, OffsetYmm = -8 }]
        };
        DevicePlcPlan.Check(args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Check_PlcWithoutRequestedDisplays_IsAccepted(bool empty)
    {
        DevicePlcPlan.Check(new()
        {
            Library = "IEC_symbol",
            PlcConnectionPoint = "X10:2",
            PlcAddress = "I0.0",
            PlcSymbolicAddress = "Start",
            PlcText = "Start input",
            DisplayProperties = empty ? [] : null
        });
    }

    [Theory]
    [InlineData("??_??@Start;en_US@Start;")]
    [InlineData("en_US@Start;??_??@Start;")]
    [InlineData("en_US@Start;")]
    public void FunctionTexts_SerializedLanguageEntries_AreRejectedAtBothWriteBoundaries(string raw)
    {
        var plc = new DevicePlaceArgs { PlcText = raw };
        var strip = new DevicePlaceArgs { TerminalStripText = raw };
        Assert.Contains("/plc_text:", Assert.Throws<BridgeErrorException>(() => DevicePlcPlan.Check(plc)).Error.Message, StringComparison.Ordinal);
        Assert.Contains("/terminal_strip_text:", Assert.Throws<BridgeErrorException>(() => DeviceTerminalPlan.Check(strip)).Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Start input")]
    [InlineData("Contact a@b; start input")]
    public void FunctionTexts_PlainIndependentText_IsAccepted(string text)
    {
        DevicePlcPlan.Check(new() { Library = "IEC_symbol", PlcConnectionPoint = "1", PlcAddress = "I0.0", PlcSymbolicAddress = "Start", PlcText = text });
        DeviceTerminalPlan.Check(new() { TerminalStrip = "-X1", TerminalNumber = "1", TerminalStripText = text });
    }

    [Theory]
    [InlineData("address")]
    [InlineData("point")]
    [InlineData("text")]
    [InlineData("symbolic")]
    [InlineData("source")]
    [InlineData("pointLength")]
    [InlineData("controls")]
    public void Check_PartialOrInvalidPlcArguments_ReportsPointer(string field)
    {
        var args = new DevicePlaceArgs { Library = "IEC_symbol", PlcConnectionPoint = "X10:2", PlcAddress = "I0.0", PlcSymbolicAddress = "Start", PlcText = "Start input" };
        switch (field)
        {
            case "address":
                args.PlcAddress = null;
                break;
            case "point":
                args.PlcConnectionPoint = null;
                break;
            case "text":
                args.PlcText = " ";
                break;
            case "symbolic":
                args.PlcSymbolicAddress = null;
                break;
            case "source":
                args.TerminalStrip = "-X1";
                break;
            case "pointLength":
                args.PlcConnectionPoint = new string('x', 65);
                break;
            case "controls":
                args.PlcText = "Start\ninput";
                break;
        }
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => DevicePlcPlan.Check(args));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains("/plc_connection_point:", error.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=GAA+A1-KF1:X10:2", "+A1-KF1:X10:2", true)]
    [InlineData("=GAA+A1-KF1:DI a.0", "+A1-KF1:DI a.0", true)]
    [InlineData("=GAA+A1-KF1:X10:2", "+A10-KF1:X10:2", false)]
    [InlineData("=GAA+A1-KF1:X10:2", "+A1-KF1:X10:3", false)]
    [InlineData("=GAA+A1-KF1:X10:2", "=OTHER+A1-KF1:X10:2", false)]
    public void PlcIdentity_ToleratesOnlyTheMissingLeadingHigherLevelFunction(string requested, string actual, bool matches) =>
        Assert.Equal(matches, DevicePlaceRequest.SameName(requested, actual));
}

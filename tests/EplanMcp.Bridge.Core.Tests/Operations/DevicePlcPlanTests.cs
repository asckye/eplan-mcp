using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DevicePlcPlanTests
{
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

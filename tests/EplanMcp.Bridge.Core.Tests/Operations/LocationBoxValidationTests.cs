using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LocationBoxValidationTests
{
    [Theory]
    [InlineData("+P1", true)]
    [InlineData("+P_1.2", true)]
    [InlineData("+", false)]
    [InlineData("=F+P1", false)]
    [InlineData("+P 1", false)]
    [InlineData("+P1-K1", false)]
    public void BridgeRechecksLocationIdentifiers(string structure, bool valid)
    {
        var args = Args();
        args.LocationBox = structure;
        if (valid)
        { DeviceSymbolPlan.CheckBox(args); }
        else
        { AssertInvalid(args, "/location_box:"); }
    }

    [Theory]
    [InlineData("part")]
    [InlineData("tag")]
    [InlineData("display")]
    [InlineData("pins")]
    [InlineData("library")]
    [InlineData("length")]
    [InlineData("missing")]
    [InlineData("nonfinite")]
    [InlineData("zero")]
    [InlineData("otherSource")]
    public void BridgeRechecksExclusiveSourceAndCorners(string problem)
    {
        var args = Args();
        switch (problem)
        {
            case "part":
                args.Part = "P";
                break;
            case "tag":
                args.Tag = "-K1";
                break;
            case "display":
                args.DisplayProperties = [];
                break;
            case "pins":
                args.ConnectionDesignations = [];
                break;
            case "library":
                args.Library = "SPECIAL";
                break;
            case "length":
                args.LocationBox = "+" + new string('a', 65);
                break;
            case "missing":
                args.CornerY = null;
                break;
            case "nonfinite":
                args.CornerX = double.PositiveInfinity;
                break;
            case "zero":
                args.CornerY = args.Y;
                break;
            case "otherSource":
                args.LocationBox = null;
                break;
        }
        AssertInvalid(args, problem is "missing" or "nonfinite" or "zero" ? "/corner_x:" : "/location_box:");
    }

    private static void AssertInvalid(DevicePlaceArgs args, string pointer)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => DeviceSymbolPlan.CheckBox(args));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.StartsWith(pointer, error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }
    private static DevicePlaceArgs Args() => new() { LocationBox = "+P1", X = 60, Y = 140, CornerX = 100, CornerY = 180 };
}

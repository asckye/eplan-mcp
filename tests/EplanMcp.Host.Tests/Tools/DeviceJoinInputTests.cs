using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class DeviceJoinInputTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinDevice_OrdinarySymbolAcceptsAllModes(bool? join)
    {
        DevicePlaceArgs args = Args();
        args.JoinDevice = join;
        Assert.Empty(DevicePlaceInputs.Check(args, null));
    }

    [Theory]
    [InlineData("part", false)]
    [InlineData("part", true)]
    [InlineData("terminal", true)]
    [InlineData("plc", true)]
    [InlineData("plcBox", true)]
    [InlineData("locationBox", true)]
    public void JoinDevice_UnsupportedRouteReportsActionableArgumentError(string route, bool join)
    {
        DevicePlaceArgs args = Args();
        args.JoinDevice = join;
        SetRoute(args, route);
        string error = Assert.Single(DevicePlaceInputs.Check(args, null), problem => problem.StartsWith("/join_device:", StringComparison.Ordinal));
        Assert.Contains("library", error, StringComparison.Ordinal);
        Assert.Contains("symbol", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, new double[] { 92, 79.1, 98.77, 79.1, 98.77, 84, 160, 84, 160, 44, 92, 44 }, null)]
    [InlineData(null, new double[] { 92, 79.1, 98.77, 79.1, 98.77, 84, 160, 84, 160, 44, 92, 44 }, "requires location_box_polyline")]
    [InlineData(true, new double[] { 92, 44, 160, 84 }, "4 to 64")]
    [InlineData(true, new double[] { 92, 44, 150, 44, 150, 84, 92, 84 }, "bounds")]
    public void LocationBoxOutline_IsCheckedAgainstTheArea(bool? polyline, double[] flat, string? expected)
    {
        var args = new DevicePlaceArgs
        {
            Project = "p",
            Page = "=A+B/1",
            LocationBox = "+F1",
            LocationBoxPolyline = polyline,
            LocationBoxOutline = [.. flat.Chunk(2)],
            X = 92,
            Y = 44,
            CornerX = 160,
            CornerY = 84,
        };
        List<string> problems = DevicePlaceInputs.Check(args, null);
        if (expected is null)
        { Assert.Empty(problems); }
        else
        { Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal)); }
    }

    [Fact]
    public void JoinDevice_AcceptsConnectionDesignationsAndDescriptions()
    {
        DevicePlaceArgs args = Args();
        args.JoinDevice = true;
        args.ConnectionDesignations = ["x1", "x2"];
        args.ConnectionDescriptions = ["In", "Out"];
        Assert.Empty(DevicePlaceInputs.Check(args, null));
    }

    private static DevicePlaceArgs Args() => new() { Project = "Line1", Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0, Tag = "-K2", X = 80, Y = 160 };

    private static void SetRoute(DevicePlaceArgs args, string route)
    {
        switch (route)
        {
            case "part":
                args.Part = "SIE.3RH2122-1HB40";
                args.Library = null;
                args.Symbol = null;
                args.SymbolVariant = null;
                break;
            case "designations":
                args.ConnectionDesignations = [];
                break;
            case "descriptions":
                args.ConnectionDescriptions = [];
                break;
            case "terminal":
                args.TerminalStrip = "-X1";
                args.TerminalNumber = "1";
                args.Tag = null;
                break;
            case "plc":
                args.PlcConnectionPoint = "1";
                break;
            case "plcBox":
                args.Library = "SPECIAL";
                args.Symbol = "PLCC";
                break;
            case "locationBox":
                args.LocationBox = "+P1";
                break;
        }
    }
}

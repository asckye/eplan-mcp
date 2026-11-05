using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class TerminalPlaceInputTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("PE_1.2", true)]
    [InlineData("", false)]
    [InlineData("1:2", false)]
    [InlineData("1/2", false)]
    [InlineData("1\n", false)]
    public void Number_IsABoundedLiteral(string number, bool valid)
    {
        DevicePlaceArgs args = Args();
        args.TerminalNumber = number;
        Assert.Equal(valid, DevicePlaceInputs.Check(args, null).Count == 0);
    }

    [Theory]
    [InlineData("part", true)]
    [InlineData("tag", false)]
    [InlineData("box", false)]
    [InlineData("symbol", false)]
    [InlineData("display", false)]
    [InlineData("pins", false)]
    [InlineData("variant", false)]
    [InlineData("nan", false)]
    [InlineData("text", false)]
    [InlineData("strip", false)]
    public void Source_AcceptsPartAndRejectsInvalidArguments(string field, bool valid)
    {
        DevicePlaceArgs args = Args();
        switch (field)
        {
            case "part":
                args.Part = "P";
                break;
            case "tag":
                args.Tag = "-X6:1";
                break;
            case "box":
                args.LocationBox = "+P1";
                break;
            case "symbol":
                args.Symbol = "X";
                break;
            case "pins":
                args.ConnectionDesignations = [];
                break;
            case "variant":
                args.SymbolVariant = -1;
                break;
            case "nan":
                args.X = double.NaN;
                break;
            case "text":
                args.TerminalStripText = new string('x', 257);
                break;
            case "strip":
                args.TerminalStrip = "-X6:1";
                break;
        }
        Assert.Equal(valid, DevicePlaceInputs.Check(args, field == "display" ? [] : null).Count == 0);
    }

    [Fact]
    public void DefinitionPosition_RequiresNewStripTextAndFinitePair()
    {
        DevicePlaceArgs args = Args();
        args.TerminalStripPosition = [72, 120];
        Assert.Contains(DevicePlaceInputs.Check(args, null), p => p.StartsWith("/terminal_strip_position:", StringComparison.Ordinal));
        args.TerminalStripText = "Fictional box";
        Assert.Empty(DevicePlaceInputs.Check(args, null));
        foreach (double[] invalid in new double[][] { [72], [72, 120, 0], [double.NaN, 120], [-1, 120], [10001, 120] })
        {
            args.TerminalStripPosition = invalid;
            Assert.Contains(DevicePlaceInputs.Check(args, null), p => p.StartsWith("/terminal_strip_position:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OptionsWithoutTerminalSource_AreRejected()
    {
        DevicePlaceArgs args = Args();
        args.TerminalStrip = null;
        args.Part = "P";
        Assert.Contains(DevicePlaceInputs.Check(args, null), s => s.StartsWith("/terminal_strip:", StringComparison.Ordinal));
    }

    private static DevicePlaceArgs Args() => new() { Project = "Line1", Page = "=F+A1/1", TerminalStrip = "-X6", TerminalNumber = "1", X = 80, Y = 160 };
}

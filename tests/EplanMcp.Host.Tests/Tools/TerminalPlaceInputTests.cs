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
    [InlineData("part")]
    [InlineData("tag")]
    [InlineData("box")]
    [InlineData("symbol")]
    [InlineData("display")]
    [InlineData("pins")]
    [InlineData("variant")]
    [InlineData("nan")]
    [InlineData("text")]
    [InlineData("strip")]
    public void Source_RejectsMixedOrInvalidArguments(string field)
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
        Assert.NotEmpty(DevicePlaceInputs.Check(args, field == "display" ? [] : null));
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

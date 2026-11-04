using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSpecialInputTests
{
    [Theory]
    [InlineData("IEC_symbol", "X2_2", 0, 160d, "1")]
    [InlineData("IEC_symbol", "XTR1", 1, 220d, "2")]
    [InlineData("IEC_symbol", "XFD", 0, 280d, "3")]
    [InlineData("GRAPHICS", "KA1_PE", 0, 340d, "4")]
    public void TerminalSymbol_ExactLiveSourcesPassInputValidation(string library, string symbol, int variant, double x, string number)
    {
        // A86's rejected requests, with the dynamic page suffix concretized as 1.
        var args = new DevicePlaceArgs
        {
            Project = "LargeWrite",
            Page = "=GAA+A1&EFS1/1",
            X = x,
            Y = 240,
            Library = library,
            Symbol = symbol,
            SymbolVariant = variant,
            TerminalStrip = "-XG81",
            TerminalNumber = number
        };
        Assert.Empty(DevicePlaceInputs.Check(args, null));
    }

    [Theory]
    [InlineData(null, "X2_2")]
    [InlineData("IEC_symbol", null)]
    [InlineData("", "X2_2")]
    [InlineData("IEC_symbol", " ")]
    [InlineData("IEC_symbol\n", "X")]
    [InlineData("IEC_symbol", "X\n")]
    public void TerminalSymbol_RequiresCompleteLibraryAndName(string? library, string? symbol)
    {
        var args = new DevicePlaceArgs { Project = "Line1", TerminalStrip = "-X6", TerminalNumber = "1", Library = library, Symbol = symbol };
        Assert.NotEmpty(DevicePlaceInputs.Check(args, null));
    }

    [Fact]
    public void TerminalSymbol_LibraryAndNameAreOptionalTogether()
    {
        var args = new DevicePlaceArgs { Project = "Line1", TerminalStrip = "-X6", TerminalNumber = "1" };
        Assert.Empty(DevicePlaceInputs.Check(args, null));
    }
}

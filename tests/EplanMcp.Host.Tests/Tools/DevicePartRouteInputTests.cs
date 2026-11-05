using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class DevicePartRouteInputTests
{
    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("IEC_symbol", "XTR1", 1, true)]
    [InlineData("IEC_symbol", null, 1, false)]
    public void ExactTerminalPartInput_ValidatesOptionalSymbol(string? library, string? symbol, int? variant, bool valid)
    {
        var args = new DevicePlaceArgs { Project = "Line1", Page = "=F+A1/1", Part = "PXC.3212140", TerminalStrip = "+A1-XQ10", TerminalNumber = "1", Library = library, Symbol = symbol, SymbolVariant = variant, X = 80, Y = 160 };
        Assert.Equal(valid, DevicePlaceInputs.Check(args, null).Count == 0);
        Assert.True(DevicePlacementRoute.UsesPart(args));
    }

    [Fact]
    public void TemplateSymbolDoesNotSilentlyIgnorePinOverrides()
    {
        var args = new DevicePlaceArgs { Project = "Line1", Part = "SIE.5SY4106-7", Library = "IEC_symbol", Symbol = "FAH1", SymbolVariant = 0, ConnectionDesignations = ["1"] };
        Assert.Contains(DevicePlaceInputs.Check(args, null), p => p.StartsWith("/display_properties:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("IEC_symbol", false, true)]
    [InlineData("IEC_symbol", true, false)]
    [InlineData("SPECIAL", false, false)]
    public void Route_UsesPartsExceptJoinedOrSpecialSymbols(string library, bool join, bool partRoute) =>
        Assert.Equal(partRoute, DevicePlacementRoute.UsesPart(new() { Part = "P", Library = library, JoinDevice = join }));
}

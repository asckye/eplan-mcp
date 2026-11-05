using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PartVariantPlacementTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(-1, false)]
    public void ExactPartVariantOnlyInput_IsAcceptedWithoutLibrary(int? variant, bool valid)
    {
        var args = new DevicePlaceArgs { Project = "Line1", Page = "=K1+A2&EFS1/2", Part = "PXC.3211813", SymbolVariant = variant, X = 80, Y = 160 };
        Assert.Equal(valid, DevicePlaceInputs.Check(args, null).Count == 0);
        Assert.True(DevicePlacementRoute.UsesPart(args));
    }

    [Theory]
    [InlineData("IEC_symbol", null)]
    [InlineData(null, "X3")]
    public void IncompleteExplicitOverride_IsStillRejected(string? library, string? symbol)
    {
        var args = new DevicePlaceArgs { Project = "Line1", Part = "PXC.3211813", Library = library, Symbol = symbol, SymbolVariant = 1 };
        Assert.NotEmpty(DevicePlaceInputs.Check(args, null));
    }
}

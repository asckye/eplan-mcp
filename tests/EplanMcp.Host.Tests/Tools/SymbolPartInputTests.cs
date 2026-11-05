using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SymbolPartInputTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void ExactBcpPartRequest_UsesSymbolRoute(bool? join)
    {
        var args = new DevicePlaceArgs
        {
            Project = "Line1",
            Library = "SPECIAL",
            Symbol = "BCP",
            SymbolVariant = 2,
            Tag = "+A1-WE1",
            JoinDevice = join,
            Part = "RIT.3451500",
            Variant = "1"
        };
        Assert.Empty(DevicePlaceInputs.Check(args, null));
        Assert.False(DevicePlacementRoute.UsesPart(args));
    }

    [Fact]
    public void JoinWithVariantButNoPart_StillRejects()
    {
        var args = new DevicePlaceArgs
        {
            Project = "Line1",
            Library = "SPECIAL",
            Symbol = "BCP",
            SymbolVariant = 2,
            Tag = "+A1-WE1",
            JoinDevice = true,
            Variant = "1"
        };
        Assert.NotEmpty(DevicePlaceInputs.Check(args, null));
    }
}

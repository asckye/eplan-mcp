using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class EplanPinsTests
{
    [Fact]
    public void ReadableDesignation_IsReturnedUnchanged()
    {
        Assert.Equal("1", EplanPins.Name(new Pin { Name = "1" }));
        Assert.Null(EplanPins.Name(null));
    }

    [Theory]
    [InlineData("S063111")]
    [InlineData("S063172")]
    public void UnreadableDesignation_IsEmptyInsteadOfFailingThePage(string code)
    {
        var pin = new Pin { Name = "1" };
        pin.NameFailure = code == "S063111" ? new EmptyPropertyException(code) : new IndexedPropertyException(code);

        Assert.Equal(string.Empty, EplanPins.Name(pin));
    }

    [Fact]
    public void AnyOtherFailure_StillReachesTheCaller()
    {
        var pin = new Pin { Name = "1", NameFailure = new InvalidOperationException("disposed") };

        Assert.Throws<InvalidOperationException>(() => EplanPins.Name(pin));
    }
}

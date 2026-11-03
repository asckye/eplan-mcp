using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // The fake page owns its placements, as EPLAN does (A39).

[Trait("Size", "Small")]
public sealed class InterruptionReadbackTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void TwoPointInterruption_ReportsConnectedPin(int index, bool atStart)
    {
        var point = new SymbolReference(83)
        {
            GraphicalConnectionPoints =
            [new() { Index = 0, Direction = "Down", Location = new(0, -1) }, new() { Index = 1, Direction = "Up", Location = new(0, 0) }],
        };
        var other = new SymbolReference(84);
        // The other symbol deliberately has the opposite pin index; ownership must be checked too.
        point.GraphicalConnections = [new()
        {
            StartSymbolRef = atStart ? point : other,
            EndSymbolRef = atStart ? other : point,
            StartConnectionPoint = new() { Index = atStart ? index : 1 - index },
            EndConnectionPoint = new() { Index = atStart ? 1 - index : index },
        }];

        PinBase actual = EplanConnectionCreator.ReadInterruptionPin(point);

        Assert.Same(point.GraphicalConnectionPoints[index], actual);
        Assert.Equal(index == 0 ? "Down" : "Up", actual.Direction);
        Assert.Equal(index == 0 ? -1 : 0, actual.Location.Y);
    }

    [Fact]
    public void TwoPointInterruption_WithoutConnectionReportsLastPin()
    {
        var point = new SymbolReference(83)
        {
            GraphicalConnectionPoints = [new() { Index = 0 }, new() { Index = 1, Direction = "Left" }],
        };

        Assert.Same(point.GraphicalConnectionPoints[1], EplanConnectionCreator.ReadInterruptionPin(point));
    }

    [Fact]
    public void SinglePointInterruption_StillReportsOnlyPin()
    {
        var point = new SymbolReference(83) { GraphicalConnectionPoints = [new() { Index = 0, Direction = "Left" }] };

        Assert.Same(point.GraphicalConnectionPoints[0], EplanConnectionCreator.ReadInterruptionPin(point));
    }
}

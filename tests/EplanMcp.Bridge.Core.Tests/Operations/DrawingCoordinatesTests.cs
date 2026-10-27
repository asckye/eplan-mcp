using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DrawingCoordinatesTests
{
    [Theory]
    [InlineData(-1.25)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(10000)]
    public void Conversion_A3Millimetres_RoundTrips(double value)
    {
        Assert.Equal(value, DrawingCoordinates.MillimetresToApi(value));
        Assert.Equal(value, DrawingCoordinates.ApiToMillimetres(DrawingCoordinates.MillimetresToApi(value)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Conversion_Nonfinite_RejectsWithPointer(double value)
    {
        Assert.Contains("/coordinate", Assert.Throws<BridgeErrorException>(() => DrawingCoordinates.ApiToMillimetres(value)).Error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Throws<BridgeErrorException>(() => DrawingCoordinates.MillimetresToApi(value));
    }

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(1.99, 4, 0)]
    [InlineData(2, 4, 4)]
    [InlineData(6, 4, 8)]
    [InlineData(1.125, 0.25, 1.25)]
    public void Snap_PageGrid_UsesNearestWithTiesAwayFromZero(double value, double grid, double expected)
    {
        Assert.Equal(expected, DrawingCoordinates.Snap(value, grid));
        Assert.Equal(value, DrawingCoordinates.Snap(value, grid, enabled: false));
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(10001, 4)]
    [InlineData(10000, 6)]
    [InlineData(1, 0)]
    [InlineData(1, -4)]
    [InlineData(1, double.NaN)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(1, double.Epsilon)]
    [InlineData(double.NaN, 4)]
    public void Snap_InvalidOrOverflowingInput_Rejects(double value, double grid) =>
        Assert.Throws<BridgeErrorException>(() => DrawingCoordinates.Snap(value, grid));

    [Fact]
    public void Bounds_FullOutlineAndFollowingText_MustBothFit()
    {
        Assert.True(DrawingCoordinates.Contains([420, 297], [[0, 0, 420, 297], [20, 20, 30, 30]]));
        Assert.False(DrawingCoordinates.Contains([420, 297], [[20, 20, 30, 30], [419, 20, 425, 25]]));
        Assert.False(DrawingCoordinates.Contains([420, 297], [[-1, 0, 10, 10]]));
        Assert.False(DrawingCoordinates.Contains([420, 297], [[0, -1, 10, 10]]));
        Assert.False(DrawingCoordinates.Contains([420, 297], [[0, 290, 10, 298]]));
    }

    [Fact]
    public void Bounds_UnknownOrMalformed_FailsClosed()
    {
        foreach (double[]? bounds in new double[]?[] { null, [], [0, 0], [2, 2, 1, 1], [0, 0, double.NaN, 1], [0, 0, 1, double.PositiveInfinity] })
        {
            Assert.False(DrawingCoordinates.Contains([420, 297], [bounds]));
        }

        Assert.False(DrawingCoordinates.Contains(null, [[0, 0, 1, 1]]));
        Assert.False(DrawingCoordinates.Contains([0, 297], [[0, 0, 1, 1]]));
        Assert.False(DrawingCoordinates.Contains([420, 297], []));
    }
}

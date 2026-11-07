namespace EplanMcp.Bridge.Core.Tests;

[Trait("Size", "Small")]
public sealed class CrossSectionsTests
{
    [Theory]
    [InlineData(1.5, "1,5")]
    [InlineData(0.75, "0,75")]
    [InlineData(10, "10")]
    [InlineData(2.5, "2,5")]
    public void Format_UsesDecimalCommaWithoutTrailingZeros(double value, string expected)
    {
        Assert.Equal(expected, CrossSections.Format(value));
    }
}

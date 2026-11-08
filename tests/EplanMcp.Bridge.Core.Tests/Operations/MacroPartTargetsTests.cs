using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class MacroPartTargetsTests
{
    [Theory]
    [InlineData(new[] { "=A-K1", "=A-K1-K1" }, new[] { "=A-K1" })]
    [InlineData(new[] { "=A-K1", "=A-K10" }, new[] { "=A-K1", "=A-K10" })]
    [InlineData(new[] { "=A-Q1", "=A-T1" }, new[] { "=A-Q1", "=A-T1" })]
    [InlineData(new[] { "=A-K1" }, new[] { "=A-K1" })]
    [InlineData(new[] { "=A-K1:1", "=A-K1-K1:2" }, new[] { "=A-K1:1" })]
    [InlineData(new[] { "=A-K1-K1", "=A-K1" }, new[] { "=A-K1" })]
    public void Select_ExcludesOnlySubDevicesAtTheDeviceBoundary(string[] tags, string[] expected)
    {
        Assert.Equal(expected, MacroPartTargets.Select(tags));
    }
}

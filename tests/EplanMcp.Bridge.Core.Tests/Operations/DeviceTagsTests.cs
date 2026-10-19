using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DeviceTagsTests
{
    [Theory]
    [InlineData(true, "=GAA+A1-FC1", true)]
    [InlineData(true, "=+-CU1", true)]
    [InlineData(false, "+A1-FC2:1", false)]
    [InlineData(true, "", false)]
    [InlineData(true, null, false)]
    public void IsDevice_IsANamedMainFunction(bool isMainFunction, string? name, bool expected)
    {
        Assert.Equal(expected, DeviceTags.IsDevice(isMainFunction, name));
    }

    [Theory]
    [InlineData("-FC1", "-FC1")]
    [InlineData(">=>+>-CU1", "=+-CU1")]
    [InlineData("=GAA+A1-FC1", "=GAA+A1-FC1")]
    [InlineData(">>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Shown_DropsEplansMarkersAndIsNullWhenNothingIsLeft(string? visibleName, string? expected)
    {
        Assert.Equal(expected, DeviceTags.Shown(visibleName));
    }
}

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

    [Fact]
    public void OnePerTag_KeepsTheShownMainFunctionAtItsTagsFirstPlace()
    {
        FunctionRecord hidden = Function("=EC1+A3-EC1-BT2", string.Empty, main: true);
        FunctionRecord coil = Function("=F+A2-KF1", "-KF1", main: true);
        FunctionRecord contact = Function("=F+A2-KF1", "-KF1", main: false);
        FunctionRecord box = Function("=EC1+A3-EC1-BT2", "-BT2", main: true);
        FunctionRecord onlyHidden = Function("=EC1+A3-EC1-MA9", ">", main: true);
        FunctionRecord onlyHiddenToo = Function("=EC1+A3-EC1-MA9", string.Empty, main: true);

        Assert.Equal([box, coil, contact, onlyHidden], DeviceTags.OnePerTag([hidden, coil, contact, box, onlyHidden, onlyHiddenToo]));
    }

    private static FunctionRecord Function(string name, string visibleName, bool main) =>
        new() { Name = name, VisibleName = visibleName, IsMain = main, Category = "Blackbox" };
}

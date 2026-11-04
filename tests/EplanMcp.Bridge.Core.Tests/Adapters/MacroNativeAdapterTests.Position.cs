using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Fake native objects are macro-owned; borrowed wrappers must not be disposed.

public sealed partial class MacroNativeAdapterTests
{
    [Theory]
    [InlineData("Overview", 0, 0, 292)]
    [InlineData("MultiLine", 0, 16, 168)]
    [InlineData("MultiLine", 1, 32, 84)]
    public void MacroPosition_ReadsSelectedReferenceAndTransientFunctionLocation(string representation, int variant, double x, double y)
    {
        ConfigureMacro();
        var type = Enum.Parse<WindowMacro.Enums.RepresentationType>(representation);
        WindowMacro.State!.ReferencePoints[(type, variant)] = new(x, y);
        WindowMacro.State.Representations[type][variant] =
        [new Function(40) { Name = "+A1-A1:1.1", FunctionCategory = FunctionCategory.PLCTerminal, Location = new(44, 132) }];

        string macro = representation == "Overview" ? "PXC/2861344.ema" : "PXC/2701290.ema";
        MacroContent content = new EplanMacroReader().ReadMacro("project", macro, representation: representation, variant: variant);

        Assert.Equal((representation, variant), (content.Representation, content.Variant));
        Assert.Equal([x, y], Assert.IsType<double[]>(content.ReferencePoint));
        Assert.Equal([44d, 132d], Assert.IsType<double[]>(Assert.Single(content.Functions).Location));
        Assert.Equal((type, variant), Assert.Single(WindowMacro.State.Selections));
    }

    [Fact]
    public void MacroPosition_UnavailableNativeCoordinatesAreNullAndOtherDevicesSurvive()
    {
        ConfigureMacro();
        WindowMacro.State!.ReferencePointError = new BaseException("Reference point unavailable");
        WindowMacro.State.Representations[WindowMacro.Enums.RepresentationType.MultiLine][0] =
        [new Function(40) { LocationError = new BaseException("Transient location unavailable") }, new Function(41) { Location = new(20, 60) }];

        MacroContent content = new EplanMacroReader().ReadMacro("project", "PXC/2701290.ema");

        Assert.Null(content.ReferencePoint);
        Assert.Null(content.Functions[0].Location);
        Assert.Equal([20d, 60d], Assert.IsType<double[]>(content.Functions[1].Location));
        Assert.Empty(WindowMacro.State.Selections);
        Assert.Equal(("MultiLine", 0), (content.Representation, content.Variant));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void MacroPosition_NonFiniteNativeCoordinatesAreNullBeforeBridgeSerialization(double x, double y)
    {
        ConfigureMacro();
        WindowMacro.State!.ReferencePoints[(WindowMacro.Enums.RepresentationType.MultiLine, 0)] = new(x, y);
        WindowMacro.State.Representations[WindowMacro.Enums.RepresentationType.MultiLine][0] =
        [new Function(40) { Location = new(x, y) }, new Function(41) { Location = new(20, 60) }];
        MacroContent content = new EplanMacroReader().ReadMacro("project", "PXC/2701290.ema");
        Assert.Null(content.ReferencePoint);
        Assert.Null(content.Functions[0].Location);
        Assert.Equal([20d, 60d], Assert.IsType<double[]>(content.Functions[1].Location));
    }

    [Fact]
    public void MacroPosition_VariantWithoutRepresentationUsesTheDefaultRepresentation()
    {
        ConfigureMacro();
        WindowMacro.State!.Representations[WindowMacro.Enums.RepresentationType.MultiLine][1] = [];
        WindowMacro.State.ReferencePoints[(WindowMacro.Enums.RepresentationType.MultiLine, 1)] = new(12, 34);
        MacroContent content = new EplanMacroReader().ReadMacro("project", "PXC/2701290.ema", variant: 1);
        Assert.Equal(("MultiLine", 1), (content.Representation, content.Variant));
        Assert.Equal([12d, 34d], Assert.IsType<double[]>(content.ReferencePoint));
    }
}

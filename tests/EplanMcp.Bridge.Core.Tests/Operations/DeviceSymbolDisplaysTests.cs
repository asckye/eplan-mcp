using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DeviceSymbolDisplaysTests
{
    [Theory]
    [InlineData("X10:2")]
    [InlineData("DI-a.0")]
    public void PlcDeviceTag_ExcludesTheConnectionPointSuffix(string point)
    {
        Assert.Equal("-KF1", DeviceSymbolDisplays.DeviceTag(new() { PlcConnectionPoint = point }, "+A1-KF1:" + point));
    }

    [Fact]
    public void ExplicitPlug_IsIndependentOfTheCardAndPinIdentity()
    {
        var args = new DevicePlaceArgs { PlcConnectionPoint = "1", PlcPlugDesignation = "-X10" };
        Assert.Equal("1", DevicePlcPlan.Suffix(args));
        Assert.Equal("+A1-KF1", DevicePlcPlan.Parent(args, "+A1-KF1:1"));
        Assert.Equal("-KF1", DeviceSymbolDisplays.DeviceTag(args, "+A1-KF1:1"));
    }

    [Theory]
    [InlineData("-KF1", true)]
    [InlineData("-KF10", false)]
    public void PlcDisplays_CardTagPlugAndNativeReference_AreComparedExactly(string shownTag, bool succeeds)
    {
        var args = new DevicePlaceArgs
        {
            PlcConnectionPoint = "X10:2",
            X = 100,
            Y = 180,
            DisplayProperties = [new() { PropertyId = 20213, OffsetYmm = -8 }, new() { PropertyId = 20406, OffsetYmm = -16 }, new() { PropertyId = 20300, OffsetYmm = -24 }]
        };
        var channel = new PlcChannel { PlugDesignation = "-X10", MainReference = "&EFA1/1.2" };
        DeviceSymbolDisplay[] displays = [Display(20213, -8, shownTag), Display(20406, -16, "-X10"), Display(20300, -24, "&EFA1/1.2")];
        void Readback() => DeviceSymbolDisplays.Verify(args, Tag, Page, channel, displays);
        if (succeeds)
        { Readback(); }
        else
        { Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(Readback).Error.Reason); }
    }

    private const string Tag = "+A1-KF1:X10:2";
    private const string Page = "=F+A1/1";
    private readonly DevicePlaceArgs _args = new()
    {
        X = 100,
        Y = 180,
        DisplayProperties = [.. new[] { 20400, 20403, 20011, 20301 }.Select((id, row) => new DeviceDisplayProperty { PropertyId = id, OffsetYmm = -8 - 8 * row })]
    };
    private readonly PlcChannel _plc = new() { Address = "I0.0", SymbolicAddress = "Calculated input", ManualSymbolicAddress = "Manual input", Text = "Start input" };

    private static List<DeviceSymbolDisplay> Displays() =>
    [
        Display(20400, -8, "I0.0"), Display(20403, -16, "Calculated input"), Display(20011, -24, "Start input"), Display(20301, -32, "")
    ];

    private static DeviceSymbolDisplay Display(int id, double y, string text) => new()
    {
        PropertyId = id,
        ParentTag = Tag,
        Page = Page,
        OnPlacedFunction = true,
        Offset = [0, y],
        Position = [100, 180 + y],
        FormatMatches = true,
        Text = text
    };

    [Fact]
    public void Verify_CalculatedAddressMayDifferFromTheManualName()
    {
        // 20403 is EPLAN's calculated value: the sensor tag, or the address when none is found.
        List<DeviceSymbolDisplay> displays = Displays();
        displays[1].Text = "I0.0";
        Verify(displays);
    }

    [Fact]
    public void Verify_NativeCalculatedAddressAndEmptyReference_AllowsDefaultsAndEnumerationOrder()
    {
        List<DeviceSymbolDisplay> displays = Displays();
        displays.Reverse();
        displays.Add(Display(20029, -4, ""));
        Verify(displays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Verify_MissingRequestedDisplay_RejectsWithItsPointer(int index)
    {
        List<DeviceSymbolDisplay> displays = Displays();
        displays.RemoveAt(index);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Verify(displays));
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
        Assert.StartsWith(FormattableString.Invariant($"/display_properties/{index}:"), error.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("parent")]
    [InlineData("page")]
    [InlineData("other_point")]
    [InlineData("offset")]
    [InlineData("position")]
    [InlineData("format")]
    [InlineData("address")]
    [InlineData("text")]
    [InlineData("reference")]
    [InlineData("nonfinite")]
    [InlineData("duplicate")]
    public void Verify_EnumeratedDisplayDiffers_Rejects(string difference)
    {
        List<DeviceSymbolDisplay> displays = Displays();
        switch (difference)
        {
            case "id":
                displays[0].PropertyId = 20029;
                break;
            case "parent":
                displays[0].ParentTag = "+A1-KF1";
                break;
            case "page":
                displays[0].Page = "=F+A1/2";
                break;
            case "other_point":
                displays[0].OnPlacedFunction = false;
                break;
            case "offset":
                displays[0].Offset[1]++;
                break;
            case "position":
                displays[0].Position[0]++;
                break;
            case "format":
                displays[0].FormatMatches = false;
                break;
            case "address":
                displays[0].Text = "";
                break;
            case "text":
                displays[2].Text = "";
                break;
            case "reference":
                displays[3].Text = Page;
                break;
            case "nonfinite":
                displays[0].Position[0] = double.NaN;
                break;
            case "duplicate":
                displays.Add(Display(20400, -8, "I0.0"));
                break;
        }
        Assert.Throws<BridgeErrorException>(() => Verify(displays));
    }

    [Fact]
    public void Verify_RepeatedRequestedDisplay_RequiresTheSameMultiplicity()
    {
        _args.DisplayProperties!.Add(new() { PropertyId = 20400, OffsetYmm = -8 });
        List<DeviceSymbolDisplay> displays = Displays();
        Assert.Throws<BridgeErrorException>(() => Verify(displays));
        displays.Add(Display(20400, -8, "I0.0"));
        Verify(displays);
    }

    [Theory]
    [InlineData("-K2", true)]
    [InlineData("-WRONG", false)]
    public void Verify_OrdinaryTagDisplay_ChecksTheReenumeratedText(string text, bool valid)
    {
        _args.DisplayProperties = [new() { PropertyId = 20214, OffsetYmm = -8 }];
        DeviceSymbolDisplay display = Display(20214, -8, text);
        display.ParentTag = "+A1-K2";
        void Check() => DeviceSymbolDisplays.Verify(_args, "=F+A1-K2", Page, null, [display]);
        if (valid)
        { Check(); }
        else
        { Assert.Throws<BridgeErrorException>(Check); }
    }

    private void Verify(IReadOnlyList<DeviceSymbolDisplay> displays) => DeviceSymbolDisplays.Verify(_args, Tag, Page, _plc, displays);
}

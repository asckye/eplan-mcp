using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DeviceSymbolDisplaysTests
{
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
    [InlineData("calculated")]
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
            case "calculated":
                displays[1].Text = _plc.ManualSymbolicAddress!;
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

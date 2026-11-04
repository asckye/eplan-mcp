using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed partial class DeviceSpecialPlanTests
{
    private const string Page = "=GAA+A1&EFS1/60";
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakePageReader _pages = new();
    private readonly FakeDeviceRenamer _names = new();
    private readonly FakePartsDatabase _parts = new();
    private readonly Source _source = new();

    public DeviceSpecialPlanTests() => _pages.Pages.Add(new PageSummary { Name = Page });

    [Fact]
    public void BlackBox_PreviewContainsResolvedTypeAndActualRequestedBoundsWithoutWriting()
    {
        SetSymbol("SPECIAL", "DC", 0, "BoxFunction", "other", "Blackbox");
        DevicePlaceArgs args = SymbolArgs("DC", "-PF9");
        args.CornerX = 140;
        args.CornerY = 180;
        WritePlan plan = Plan(args);

        JObject symbol = JObject.Parse(plan.Details!["symbol"]);
        Assert.Equal("BoxFunction", (string?)symbol["type"]);
        Assert.Equal("Blackbox", (string?)symbol["functionCategory"]);
        JObject box = JObject.Parse(plan.Details["blackBox"]);
        Assert.Equal("=GAA+A1-PF9", (string?)box["tag"]);
        Assert.Equal([80d, 140d, 140d, 180d], box["bounds"]!.Values<double>());
        Assert.Empty(_parts.Reads);
        Assert.Single(plan.Changes);
    }

    [Theory]
    [InlineData("DCP", 0, 84d, 160d)]
    [InlineData("DCP", 2, 84d, 160d)]
    [InlineData("DCP", 4, 84d, 160d)]
    [InlineData("DCPP3", 0, 84d, 160d)]
    [InlineData("DCP", 0, 240d, 60d)]
    public void DeviceConnectionPoint_PreviewAllowsExistingBlackBoxTagInsideOrOutside(string name, int variant, double x, double y)
    {
        _names.Functions.Add(("=GAA+A1-PF9", "1/1/1/0", true, Page, "PF9"));
        _names.Functions.Add(("=GAA+A1-PF9:1", "1/1/2/0", false, Page, "PF9:1"));
        SetSymbol("SPECIAL", name, variant, "Function", "function", "DeviceConnectionPoint");
        DevicePlaceArgs args = SymbolArgs(name, "-PF9");
        args.SymbolVariant = variant;
        args.ConnectionDesignations = ["2"];
        args.X = x;
        args.Y = y;

        WritePlan plan = Plan(args);

        Assert.Equal("=GAA+A1-PF9:2", plan.Details!["tag"]);
        Assert.Equal("-PF9", args.Tag);
        Assert.Contains("SPECIAL/" + name, Assert.Single(plan.Changes).NewValue, StringComparison.Ordinal);
        Assert.Empty(_parts.Reads);
    }

    [Fact]
    public void DeviceConnectionPoint_NeedsOneNewPinDesignation()
    {
        _names.Functions.Add(("=GAA+A1-PF9", "1/1/1/0", true, Page, "PF9"));
        _names.Functions.Add(("=GAA+A1-PF9:1", "1/1/2/0", false, Page, "PF9:1"));
        SetSymbol("SPECIAL", "DCP", 0, "Function", "function", "DeviceConnectionPoint");
        DevicePlaceArgs missing = SymbolArgs("DCP", "-PF9");
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(missing)).Error.Reason);
        DevicePlaceArgs repeated = SymbolArgs("DCP", "-PF9");
        repeated.ConnectionDesignations = ["1"];
        Assert.Equal(ErrorReason.AlreadyExists, Assert.Throws<BridgeErrorException>(() => Plan(repeated)).Error.Reason);
    }

    [Fact]
    public void DeviceConnectionPoint_ExactLiveColonTagStillFailsBeforeSymbolPlacement()
    {
        // A86 live failing request: SPECIAL/DCP A at (40, 240), tag -PF9:1.
        SetSymbol("SPECIAL", "DCP", 0, "Function", "function", "DeviceConnectionPoint");
        DevicePlaceArgs args = SymbolArgs("DCP", "-PF9:1");
        args.X = 40;
        args.Y = 240;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("must not contain ':'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlackBox_CannotReuseAnExistingDeviceTag()
    {
        _names.Functions.Add(("=GAA+A1-PF9", "1/1/1/0", true, Page, "PF9"));
        SetSymbol("SPECIAL", "DC", 0, "BoxFunction", "other", "Blackbox");
        DevicePlaceArgs args = SymbolArgs("DC", "-PF9");
        args.CornerX = 140;
        args.CornerY = 180;
        Assert.Equal(ErrorReason.AlreadyExists, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 180d)]
    [InlineData(140d, null)]
    [InlineData(80d, 180d)]
    [InlineData(140d, 140d)]
    [InlineData(double.NaN, 180d)]
    public void BlackBox_MissingOrInvalidCornersFailBeforeSymbolLookup(double? x, double? y)
    {
        DevicePlaceArgs args = SymbolArgs("DC", "-PF9");
        args.CornerX = x;
        args.CornerY = y;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/corner_x:", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void BlackBox_MissingTagFailsBeforeSymbolLookup(string? tag)
    {
        DevicePlaceArgs args = SymbolArgs("DC", tag);
        args.CornerX = 140;
        args.CornerY = 180;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'tag'", error.Message, StringComparison.Ordinal);
    }

    private WritePlan Plan(DevicePlaceArgs args) => Assert.IsType<WritePlan>(
        new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source).Execute(args));

    private void SetSymbol(string library, string name, int variant, string type, string kind, string? category) =>
        _source.Symbol = new() { Library = library, Name = name, Variant = variant, Type = type, Kind = kind, FunctionCategory = category, ConnectionPoints = name.StartsWith("DCP", StringComparison.Ordinal) ? [new() { Location = [0, 3], Direction = "Up" }] : [] };

    private static DevicePlaceArgs SymbolArgs(string symbol, string? tag) => new()
    { Project = "Line1", Page = Page, Library = "SPECIAL", Symbol = symbol, SymbolVariant = 0, Tag = tag, X = 80, Y = 140 };

    private sealed class Source : IDevicePlacer, ISymbolReader
    {
        public SymbolSearchItem Symbol { get; set; } = new();
        public MacroTargetPage? ReadPage(string projectPath, string pageName) =>
            LayoutFixture.Target(new() { Name = pageName, Type = "Circuit", Width = 420, Height = 297 });
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => [Symbol];
        public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag) => throw new NotSupportedException();
        public void ReadCommitted(string projectPath, PlacedDevice result) => throw new NotSupportedException();
    }
}

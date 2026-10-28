using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DeviceSymbolExecutionTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakePageReader _pages = new();
    private readonly FakeDeviceRenamer _names = new();
    private readonly FakePartsDatabase _parts = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Source _source = new();

    public DeviceSymbolExecutionTests() => _pages.Pages.Add(new PageSummary { Name = "=F+A1/1" });

    [Fact]
    public void Execute_PartWithPartialSymbolSource_RejectsBeforeReadingParts()
    {
        DevicePlaceArgs args = Args();
        args.Library = null;
        args.Part = "SIE.3RH2122-1HB40";
        AssertRejected(args, "/library");
        Assert.Empty(_parts.Reads);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.MaxValue, double.MaxValue)]
    public void Execute_InvalidOffsetsOrOverflow_RejectsBeforeTransaction(double offset, double x)
    {
        DevicePlaceArgs args = Args();
        args.X = x;
        args.DisplayProperties = [new() { PropertyId = 20214, OffsetXmm = offset }];
        AssertRejected(args, "/display_properties");
    }

    [Theory]
    [InlineData(20022, 1)]
    [InlineData(20214, 9)]
    public void Execute_UnknownDisplayPropertyOrExcessivePlacements_RejectsBeforeTransaction(int id, int count)
    {
        DevicePlaceArgs args = Args();
        args.DisplayProperties = [.. Enumerable.Range(0, count).Select(_ => new DeviceDisplayProperty { PropertyId = id })];
        AssertRejected(args, "/display_properties");
    }

    [Theory]
    [InlineData("")]
    [InlineData("A\n1")]
    [InlineData(" ")]
    public void Execute_InvalidDesignation_RejectsBeforeTransaction(string text)
    {
        DevicePlaceArgs args = Args();
        args.ConnectionDesignations = [text];
        AssertRejected(args, "/connection_designations");
    }

    [Fact]
    public void Execute_ChangedRequestedDesignation_IsStaleBeforeOpeningTheSymbol()
    {
        DevicePlaceArgs args = Args();
        args.ConnectionDesignations = ["A1"];
        args.Fingerprint = ((WritePlan)new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source).Execute(args)!).Fingerprint;
        args.ConnectionDesignations[0] = "13";
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Empty(_transactions.Calls);
    }

    private void AssertRejected(DevicePlaceArgs args, string pointer)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Contains(pointer, error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_transactions.Calls);
    }
    private object? Execute(DevicePlaceArgs args) => new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _source, _transactions, _source,
        (_, _, _) => throw new InvalidOperationException("Must reject before resolving native objects.")).Execute(args);
    private static DevicePlaceArgs Args() => new() { Project = "Line1", Page = "=F+A1/1", Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0, Tag = "-K1", Fingerprint = "old" };

    private sealed class Source : IDevicePlacer, ISymbolReader
    {
        public MacroTargetPage? ReadPage(string projectPath, string pageName) => new() { Name = pageName, Type = "Circuit" };
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => [new() { Library = "IEC_symbol", Name = "K", Type = "Function", Kind = "function", ConnectionPoints = [new() { Location = [0, 0] }] }];
        public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag) => throw new NotSupportedException();
    }
}

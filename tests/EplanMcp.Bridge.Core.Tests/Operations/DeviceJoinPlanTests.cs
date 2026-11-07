using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DeviceJoinPlanTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakePageReader _pages = new();
    private readonly FakeDeviceRenamer _names = new();
    private readonly FakePartsDatabase _parts = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Source _source = new();

    public DeviceJoinPlanTests() => _pages.Pages.Add(new() { Name = "=F+A1/1" });

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void ExistingTag_StillRefusesUnlessJoinIsTrue(bool? join)
    {
        Existing("=F+A1-K2");
        DevicePlaceArgs args = Args();
        args.JoinDevice = join;
        Assert.Equal(ErrorReason.AlreadyExists, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("=F+A1-K2")]
    [InlineData("=F+A1-k2")]
    public void Join_ExistingTagUsesItsCanonicalName(string existing)
    {
        Existing(existing);
        WritePlan plan = Plan(Args());
        Assert.Equal(existing, plan.Details!["tag"]);
        Assert.Contains(plan.Changes, change => change.NewValue!.Contains(existing, StringComparison.Ordinal));
        Assert.Empty(_parts.Reads);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("=F+A1-K2:13")]
    [InlineData("=F+A1-K20")]
    public void Join_MissingExactDeviceRefusesWithoutStarting(string? existing)
    {
        if (existing is not null)
        { Existing(existing); }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;
        Assert.Equal(ErrorReason.DeviceNotFound, error.Reason);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("part", false)]
    [InlineData("part", true)]
    [InlineData("terminal", true)]
    [InlineData("plc", true)]
    [InlineData("plcBox", true)]
    public void Join_UnsupportedInputsRejectAtTheBridgeBoundary(string route, bool join)
    {
        DevicePlaceArgs args = Args();
        args.JoinDevice = join;
        switch (route)
        {
            case "part":
                args.Part = "SIE.3RH2122-1HB40";
                args.Library = null;
                args.Symbol = null;
                args.SymbolVariant = null;
                break;
            case "designations":
                args.ConnectionDesignations = [];
                break;
            case "descriptions":
                args.ConnectionDescriptions = [];
                break;
            case "terminal":
                args.TerminalStrip = "-X1";
                args.TerminalNumber = "1";
                args.Tag = null;
                break;
            case "plc":
                args.PlcConnectionPoint = "1";
                break;
            case "plcBox":
                args.Library = "SPECIAL";
                args.Symbol = "PLCC";
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/join_device:", error.Message, StringComparison.Ordinal);
        Assert.Contains("library", error.Message, StringComparison.Ordinal);
        Assert.Contains("symbol", error.Message, StringComparison.Ordinal);
        Assert.Empty(_parts.Reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectWithoutStructure_BareTagCountsAsTheDashedTag(bool join)
    {
        _pages.Pages.Add(new() { Name = "5" });
        _names.Functions.Add(("M1", "1/17/1/0", true, "4", "M1"));
        DevicePlaceArgs args = Args();
        args.Page = "5";
        args.Tag = "-M1";
        args.JoinDevice = join;
        if (join)
        {
            Assert.Equal("M1", Plan(args).Details!["tag"]);
        }
        else
        {
            Assert.Equal(ErrorReason.AlreadyExists, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        }
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Join_ReadsResolvedPlcCategoryBeforeOpeningATransaction()
    {
        Existing("=F+A1-K2");
        _source.Symbol.FunctionCategory = "PLCTerminal";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Join_ChangingToFalseAfterPlanningRefusesAsStaleBeforeWriting()
    {
        Existing("=F+A1-K2");
        DevicePlaceArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        args.JoinDevice = false;
        var operation = new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _source, _transactions, _source,
            (_, _, _, _) => throw new InvalidOperationException("A stale plan must not open native objects."));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Join_ExecutionCommitsAuxiliaryReadbackAndRollsBackAMainFunction(bool main)
    {
        Existing("=F+A1-K2");
        DevicePlaceArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        var placement = new Placement(_transactions, main);
        var operation = new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _source, _transactions, _source, (_, _, _, _) => placement);
        if (main)
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error;
            Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
            Assert.Contains("Rollback", _transactions.Calls);
            Assert.DoesNotContain("Commit", _transactions.Calls);
        }
        else
        {
            DevicePlaceData result = Assert.IsType<DevicePlaceData>(operation.Execute(args));
            Assert.Equal("=F+A1-K2", result.Tag);
            Assert.False(result.IsMainFunction);
            Assert.Contains("Commit", _transactions.Calls);
        }
    }

    private void Existing(string tag) => _names.Functions.Add((tag, "1/17/1/0", true, "=F+A1/1", "K2"));
    private WritePlan Plan(DevicePlaceArgs args) => Assert.IsType<WritePlan>(new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source).Execute(args));
    private static DevicePlaceArgs Args() => new() { Project = "Line1", Page = "=F+A1/1", Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0, Tag = "-K2", JoinDevice = true, X = 80, Y = 160 };

    private sealed class Source : IDevicePlacer, ISymbolReader
    {
        internal SymbolSearchItem Symbol { get; } = new() { Library = "IEC_symbol", Name = "K", Type = "Function", Kind = "function" };
        public MacroTargetPage? ReadPage(string projectPath, string pageName) => LayoutFixture.Target(new() { Name = pageName, Type = "Circuit" });
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => [Symbol];
        public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag, DevicePlaceArgs? options = null) => throw new NotSupportedException();
        public void ReadCommitted(string projectPath, PlacedDevice result) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinContact_AssignsAfterCommitBeforeReadback(bool withPins)
    {
        Existing("=F+A1-K2");
        DevicePlaceArgs args = Args();
        args.Symbol = _source.Symbol.Name = "S";
        _source.Symbol.ConnectionPoints = [new() { Location = [0, 0] }, new() { Location = [0, 4] }];
        args.ConnectionDesignations = withPins ? ["1/L1", "2/T1"] : null;
        args.Fingerprint = Plan(args).Fingerprint;
        var placement = new Placement(_transactions, false);
        var operation = new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _source, _transactions, _source, (_, _, _, _) => placement);
        var result = Assert.IsType<DevicePlaceData>(operation.Execute(args));
        Assert.True(result.AssignedFunction);
        Assert.Equal(["1/L1", "2/T1"], result.Pins!);
        Assert.True(placement.ReadBack);
    }

    private sealed class Placement(FakeTransactions transactions, bool main) : IDeviceSymbolPlacement, IDeviceSymbolCommittedReadback
    {
        public bool ReadBack { get; private set; }
        public void AttachPart(DevicePlaceArgs args, DevicePlaceData data) { }
        public void AssignUnplacedFunction(DevicePlaceArgs args, DevicePlaceData data)
        {
            Assert.Contains("Commit", transactions.Calls);
            Assert.DoesNotContain("CloseUndo", transactions.Calls);
            Assert.False(ReadBack);
            data.AssignedFunction = true;
            data.Pins = args.ConnectionDesignations ?? ["1/L1", "2/T1"];
        }
        public void ReadCommitted(DevicePlaceArgs args, DevicePlaceData data)
        { Assert.Contains("CloseUndo", transactions.Calls); ReadBack = true; }
        public ITransactionScope Begin(string undoDescription) => transactions.Begin(undoDescription);
        public DevicePlaceData Place(DevicePlaceArgs args, string tag) => new() { Tag = tag, Page = args.Page, IsMainFunction = main, Functions = 1 };
        public IReadOnlyList<DeviceSymbolDisplay> ReadDisplays() => [];
    }
}

using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed partial class DeviceSymbolExecutionTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1" });
    private readonly FakePageReader _pages = new();
    private readonly FakeDeviceRenamer _names = new();
    private readonly FakePartsDatabase _parts = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Source _source = new();

    public DeviceSymbolExecutionTests() => _pages.Pages.Add(new PageSummary { Name = "=F+A1/1" });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayOffsets_TextAcrossLive248Wire_RefusesOnlyExplicitPosition(bool explicitPosition)
    {
        LayoutGuard.Model layout = LayoutGuardDefaultTextTests.Model();
        DrawingPlacement text = LayoutGuardDefaultTextTests.CableText(layout);
        text.Properties["property_id"] = "20214";
        layout.Page.Placements.Clear();
        DevicePlaceArgs args = Args();
        args.X = 268;
        args.Y = 92;
        if (explicitPosition)
        { args.DisplayProperties = [new() { PropertyId = 20214, OffsetXmm = 4 }]; }
        _source.Target = LayoutFixture.Target(new() { Name = args.Page, Type = "Circuit", Layout = layout, PredictDevice = _ => [text] });
        var operation = new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source);
        if (explicitPosition)
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error;
            Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
            Assert.Contains("R3: Text bounds conflict with a line.", error.Message, StringComparison.Ordinal);
        }
        else
        {
            var plan = Assert.IsType<WritePlan>(operation.Execute(args));
            Assert.Contains("R3: Warning:", plan.Details!["layoutWarnings"], StringComparison.Ordinal);
        }
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("=GAA+A1-D3KF3", null, true, null)]
    [InlineData("=GAA+A1-D3KF3", "-X10", true, null)]
    [InlineData("=GAA+A1-D3KF5", "-X10", true, "CloseUndo")]
    [InlineData("=GAA+A1-D3KF5", "-X10", false, null)]
    [InlineData("=GAA+A1-D3KF5", "-X10", false, "Commit")]
    public void Execute_D3CommittedReadbackFailure_WarnsButSuccessfulReadbackWorks_CommitFailureStaysUnknown(string tag, string? plug, bool lookupMiss, string? transactionFailure)
    {
        var args = new DevicePlaceArgs
        {
            Project = "Line1",
            Page = "=GAA+A1&EFS1/3",
            Library = "IEC_symbol",
            Symbol = "PLC_CBOX",
            SymbolVariant = 0,
            Tag = tag,
            PlcConnectionPoint = "1",
            PlcAddress = "I0.1",
            PlcSymbolicAddress = "FICTIONAL_DI_1",
            PlcText = "Fictional D3 input",
            PlcPlugDesignation = plug,
            X = 120,
            Y = 176
        };
        _pages.Pages.Add(new() { Name = args.Page });
        _names.Functions.Add((tag, "1/17/1/0", true, "=GAA+A1&EFS1/4", "D3KF"));
        _source.Symbol.Name = args.Symbol;
        _source.Symbol.FunctionCategory = "PLCTerminal";
        _source.Symbol.PlcInputDefinition = "1/2";
        args.Fingerprint = ((WritePlan)new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source, _source).Execute(args)!).Fingerprint;
        var placement = new CommittedPlacement(_transactions, lookupMiss);
        _transactions.FailAt = transactionFailure;
        var operation = new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _source, _transactions, _source, (_, _, _, _) => placement, _source);

        if (transactionFailure == "Commit")
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error;
            Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
            Assert.False(placement.ReadCommittedCalled);
            return;
        }
        DevicePlaceData data = Assert.IsType<DevicePlaceData>(operation.Execute(args));
        Assert.True(placement.ReadCommittedCalled);
        Assert.Contains("Layout before write:", data.Warning, StringComparison.Ordinal);
        Assert.Contains("Layout after write:", data.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
        Assert.Equal(["Begin AI: eplan_device_place", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        if (lookupMiss)
        {
            Assert.Contains("was committed", data.Warning, StringComparison.Ordinal);
            Assert.Contains("plug designation", data.Warning, StringComparison.Ordinal);
            Assert.Contains("native references", data.Warning, StringComparison.Ordinal);
            Assert.DoesNotContain("Sequence", data.Warning, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("was committed", data.Warning, StringComparison.Ordinal);
            Assert.Equal("&EFS1/4.2", data.Plc!.MainReference);
            Assert.Equal(plug, data.Plc.PlugDesignation);
        }
        if (transactionFailure == "CloseUndo")
        { Assert.Contains("did not close its undo step", data.Warning, StringComparison.Ordinal); }
    }

    [Fact]
    public void Plan_D3PlugRequest_KeepsNativePinSuffixAndIndependentPlug()
    {
        // Exact fictional request in q-d3-PlcDisplaySpikeTests.log; the native 20022 write overwrites Name's suffix.
        var args = new DevicePlaceArgs
        {
            Project = "Line1",
            Page = "=GAA+A1&EFS1/3",
            Library = "IEC_symbol",
            Symbol = "PLC_CBOX",
            SymbolVariant = 0,
            Tag = "=GAA+A1-D3KF3",
            PlcConnectionPoint = "1",
            PlcAddress = "I0.1",
            PlcSymbolicAddress = "FICTIONAL_DI_1",
            PlcText = "Fictional D3 input",
            X = 120,
            Y = 176,
            PlcPlugDesignation = "-X10",
            DisplayProperties = [new() { PropertyId = 20213, OffsetXmm = 4, OffsetYmm = 12 }, new() { PropertyId = 20300, OffsetXmm = 4, OffsetYmm = 8 }, new() { PropertyId = 20406, OffsetXmm = 4, OffsetYmm = 4 }, new() { PropertyId = 20400, OffsetXmm = 4 }]
        };
        _pages.Pages.Add(new() { Name = args.Page });
        _names.Functions.Add((args.Tag!, "1/17/1/0", true, "=GAA+A1&EFS1/4", "D3KF3"));
        _source.Symbol.Name = args.Symbol;
        _source.Symbol.FunctionCategory = "PLCTerminal";
        _source.Symbol.PlcInputDefinition = "1/2";

        WritePlan plan = (WritePlan)new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source, _source).Execute(args)!;

        Assert.Equal("=GAA+A1-D3KF3:1", plan.Details!["tag"]);
        Assert.Contains(plan.Changes, c => c.Property == "20022[1]" && c.NewValue == "1");
        Assert.Contains(plan.Changes, c => c.Property == "20406" && c.NewValue == "-X10");
        Assert.Equal(["1"], args.ConnectionDesignations!);
        _names.Functions.Add(("=GAA+A1-D3KF3:1", "1/17/2/0", false, args.Page, "D3KF3:1"));
        var repeated = Assert.IsType<WritePlan>(new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _source, _source, _source).Execute(args));
        Assert.Contains("category is unreadable", repeated.Details!["layoutWarnings"], StringComparison.Ordinal);
        Assert.Empty(_transactions.Calls);
    }

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
        (_, _, _, _) => throw new InvalidOperationException("Must reject before resolving native objects.")).Execute(args);
    private static DevicePlaceArgs Args() => new() { Project = "Line1", Page = "=F+A1/1", Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0, Tag = "-K1", Fingerprint = "old" };

    private sealed class Source : IDevicePlacer, ISymbolReader, IPlcReader
    {
        public void ReadCommitted(string projectPath, PlacedDevice result) => throw new NotSupportedException();
        internal MacroTargetPage? Target { get; set; }
        public MacroTargetPage? ReadPage(string projectPath, string pageName) => Target ?? LayoutFixture.Target(new() { Name = pageName, Type = "Circuit" });
        public SymbolSearchItem Symbol { get; } = new() { Library = "IEC_symbol", Name = "K", Type = "Function", Kind = "function", ConnectionPoints = [new() { Location = [0, 0] }] };
        public PlcRecord? ReadPlc(string projectPath, string tag, string? language, bool detailed = false) => new() { Plc = new() { Tag = tag } };
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => [Symbol];
        public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag, DevicePlaceArgs? options = null) => throw new NotSupportedException();
    }

    private sealed class CommittedPlacement(FakeTransactions transactions, bool lookupMiss) : IDeviceSymbolPlacement, IDeviceSymbolCommittedReadback
    {
        private PlcChannel? _channel;
        public bool ReadCommittedCalled { get; private set; }
        public ITransactionScope Begin(string undoDescription) => transactions.Begin(undoDescription);
        public DevicePlaceData Place(DevicePlaceArgs args, string tag)
        {
            _channel = new()
            {
                Name = tag,
                Page = args.Page,
                Owner = args.Tag,
                Address = args.PlcAddress,
                ManualSymbolicAddress = args.PlcSymbolicAddress,
                Text = args.PlcText,
                PlugDesignation = args.PlcPlugDesignation
            };
            return new() { Tag = tag, Page = args.Page, Category = "PLCTerminal", Plc = _channel };
        }
        public void ReadCommitted(DevicePlaceArgs args, DevicePlaceData data)
        {
            ReadCommittedCalled = true;
            if (lookupMiss)
            { data.Plc = Array.Empty<PlcChannel>().Single(c => c.Name == data.Tag); }
            else
            {
                _channel!.MainReference = "&EFS1/4.2";
                data.Plc = _channel;
            }
        }
        public IReadOnlyList<DeviceSymbolDisplay> ReadDisplays() => [];
    }
}

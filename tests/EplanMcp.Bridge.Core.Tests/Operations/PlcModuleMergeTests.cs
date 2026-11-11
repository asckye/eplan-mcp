using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlcModuleMergeTests
{
    private const string Source = "=GAA+A1-A?", Target = "=GAA+A1-A1";
    private readonly Store _store = new();
    private readonly FakeTransactions _transactions = new();
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = "project", Current = true });
    private readonly FakeDeviceReader _reader = new();
    private Action? _undo;

    public PlcModuleMergeTests()
    {
        _store.Functions.AddRange([
            new() { Name = Source + ":1.1", Id = "point1", Category = "PLCTerminal", Page = "circuit" },
            new() { Name = Source + ":1.2", Id = "point2", Category = "PLCTerminal", Page = "circuit" },
            new() { Name = Target, Id = "box", Category = "PLCBox", IsMain = true, Page = "overview" },
            new() { Name = Target + ":1.1", Id = "overview", Category = "PLCTerminal", Page = "overview" },
        ]);
        _transactions.Snapshot = () =>
        {
            var before = _store.Functions.Select(Store.Copy).ToList();
            return _undo = () => { _store.Functions.Clear(); _store.Functions.AddRange(before.Select(Store.Copy)); };
        };
    }

    [Fact]
    public void BriefInput_TerminalOnlyModuleJoinsExistingBox_PreservingBothPagesAndOneUndo()
    {
        DeviceUpdateArgs args = Args();
        WritePlan plan = Plan(args);
        Assert.Equal("2", plan.Details!["functions"]);
        Assert.Equal("circuit", plan.Details["pages"]);
        args.Fingerprint = plan.Fingerprint;
        DeviceUpdateData result = Execute(args);
        Assert.Equal((Target, Source, 2), (result.Tag, result.PreviousTag, result.Functions));
        Assert.Equal([Target + ":1.1", Target + ":1.2"], _store.Functions.Where(f => f.Page == "circuit").Select(f => f.Name));
        Assert.Equal(Target + ":1.1", _store.Functions.Single(f => f.Id == "overview").Name);
        Assert.Equal(["Begin AI: eplan_device_update", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        _undo!();
        Assert.Equal(Source + ":1.1", _store.Functions[0].Name);
        Assert.Equal(Target + ":1.1", _store.Functions.Single(f => f.Id == "overview").Name);
    }

    [Fact]
    public void DriveControlMacro_WithItsBoxAndShieldPoint_PlansToJoinTheDrive()
    {
        // G120C MultiLine variant 1 (2026-11-10): a PLC box, PLC points and the shield SH(3) as a device connection point,
        // none of them main; the drive's power macro already holds the main PLC box under the target tag.
        _store.Functions.InsertRange(0, [
            new() { Name = Source, Id = "control box", Category = "PLCBox", Page = "circuit" },
            new() { Name = Source + ":SH(3)", Id = "shield", Category = "DeviceEndTerminal", Page = "circuit" },
        ]);
        DeviceUpdateArgs args = Args();

        WritePlan plan = Plan(args);

        Assert.Equal("4", plan.Details!["functions"]);
        Assert.Equal("circuit", plan.Details["pages"]);
    }

    [Fact]
    public void ModuleWithAnOrdinaryFunction_IsNotJoined()
    {
        _store.Functions.Insert(0, new() { Name = Source + ":13", Id = "contact", Category = "NoContact", Page = "circuit" });
        Assert.Equal(ErrorReason.AlreadyExists, Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error.Reason);
    }

    [Fact]
    public void TerminalOnlyModule_TakesATagNoCardHasYet()
    {
        // The sample's =F+A2-KE1: its Overview page is outside the redrawn pages, so the module's points come first.
        _store.Functions.RemoveAll(f => f.Id is "box" or "overview");
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;

        DeviceUpdateData result = Execute(args);

        Assert.Equal((Target, Source, 2), (result.Tag, result.PreviousTag, result.Functions));
        Assert.Equal([Target + ":1.1", Target + ":1.2"], _store.Functions.Select(f => f.Name));
    }

    [Fact]
    public void CommittedReadbackAcceptsChangedSessionIdsForSourceAndDestination()
    {
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _store.AfterRename = () =>
        {
            foreach (DeviceNames.NamedFunction function in _store.Functions)
            { function.Id = "committed/" + function.Id; }
        };

        DeviceUpdateData result = Execute(args);

        Assert.Equal((Target, 2), (result.Tag, result.Functions));
        Assert.All(_store.Functions, f => Assert.StartsWith("committed/", f.Id, StringComparison.Ordinal));
        Assert.Equal(2, _store.Functions.Count(f => f.Page == "overview"));
        Assert.DoesNotContain(_transactions.Calls, call => call == "Rollback");
    }

    [Theory]
    [InlineData(true, "PLCTerminal", "PLCBox")]
    [InlineData(false, "BusTerminal", "PLCBox")]
    [InlineData(false, "Coil", "PLCBox")]
    [InlineData(false, null, "PLCBox")]
    [InlineData(false, "PLCTerminal", "Coil")]
    public void OtherCollisionsStillReturnAlreadyExists(bool main, string? sourceCategory, string targetCategory)
    {
        _store.Functions[0].IsMain = main;
        _store.Functions[0].Category = sourceCategory;
        _store.Functions[2].Category = targetCategory;
        Assert.Equal(ErrorReason.AlreadyExists, Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error.Reason);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceOrDestinationChangesInvalidatePlan(bool destination)
    {
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _store.Functions[destination ? 3 : 0].Id += "changed";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("leftBehind")]
    [InlineData("overviewChanged")]
    [InlineData("suffixChanged")]
    public void IncompleteMergeOrChangedOverviewRollsBack(string failure)
    {
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _store.AfterRename = () =>
        {
            if (failure == "leftBehind")
            { _store.Functions[0].Name = Source + ":1.1"; }
            if (failure == "overviewChanged")
            { _store.Functions[3].Page = "wrong"; }
            if (failure == "suffixChanged")
            { _store.Functions[1].Name = Target + ":99"; }
        };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(Source + ":1.1", _store.Functions[0].Name);
        Assert.Equal("overview", _store.Functions[3].Page);
    }

    private static DeviceUpdateArgs Args() => new() { Project = "Line1", Tag = Source, NewTag = Target };
    private WritePlan Plan(DeviceUpdateArgs args) => (WritePlan)new DeviceUpdatePlanOperation(_catalog, _reader, _store).Execute(args)!;
    private DeviceUpdateData Execute(DeviceUpdateArgs args) => (DeviceUpdateData)new DeviceUpdateExecuteOperation(_catalog, _reader, _store, _transactions).Execute(args)!;

    private sealed class Store : IDeviceRenamer
    {
        public List<DeviceNames.NamedFunction> Functions { get; } = [];
        public Action? AfterRename { get; set; }
        public DeviceNames? ReadDevice(string projectPath, string tag)
        {
            var named = Functions.Where(f => f.Name == tag || f.Name.StartsWith(tag + ":", StringComparison.Ordinal)).ToList();
            return named.Count == 0 ? null : new() { Tag = tag, Functions = [.. named.Select(Copy)] };
        }
        public IReadOnlyList<string> FunctionNames(string projectPath) => [.. Functions.Select(f => f.Name)];
        public RenameReadBack Rename(string projectPath, string tag, string newTag)
        {
            foreach (DeviceNames.NamedFunction function in Functions.Where(f => f.Name.StartsWith(tag + ":", StringComparison.Ordinal)))
            { function.Name = newTag + function.Name[tag.Length..]; }
            AfterRename?.Invoke();
            return new() { MainName = newTag + ":1.1" };
        }
        public static DeviceNames.NamedFunction Copy(DeviceNames.NamedFunction f) => new() { Name = f.Name, Id = f.Id, IsMain = f.IsMain, Category = f.Category, Page = f.Page };
    }
}

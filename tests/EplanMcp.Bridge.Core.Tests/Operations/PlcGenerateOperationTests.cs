using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>plc.generate.plan and plc.generate.execute against fakes.</summary>
[Trait("Size", "Small")]
public sealed class PlcGenerateOperationTests
{
    private const string Device = "=F+A1-A1";
    private const string Target = "=F+A1&EFS1/9";

    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    private readonly FakePageReader _pages = new();
    private readonly Schematics _schematics = new();
    private readonly string _macroRoot = Directory.CreateTempSubdirectory("eplanmcp-plc-").FullName;
    private readonly FakeMacroReader _macros;

    public PlcGenerateOperationTests()
    {
        _macros = new FakeMacroReader(_macroRoot);
        _pages.Pages.Add(new PageSummary { Name = "=F+A1&EFA1/1" });
    }

    [Fact]
    public void Plan_ListsThePageWithDeviceAndMacro_AndWritesNothing()
    {
        PlannedChange change = Assert.Single(Plan(Args()).Changes);
        Assert.Equal(("create", "page", Target), (change.Action, change.Kind, change.Name));
        Assert.Equal(Device + @" from $(MD_MACROS)\PXC\1.ema", change.NewValue);
        Assert.Empty(_schematics.Generated);
    }

    [Fact]
    public void Plan_TheDeviceAsTheProjectSpellsIt_IgnoresCase()
    {
        PlcGenerateArgs args = Args();
        args.Devices![0].Tag = Device.ToLowerInvariant();
        Assert.Contains(Device + " from", Assert.Single(Plan(args).Changes).NewValue, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=F+A1-A9", Target, "not a PLC device")]
    [InlineData(Device, "=F+A1&EFA1/1", "already exists")]
    [InlineData(Device, "no page", "not a full page name")]
    [InlineData("", Target, "is required")]
    public void Plan_ABadDeviceOrPage_IsInvalid(string tag, string page, string expected)
    {
        var args = new PlcGenerateArgs { Project = "Line1", Devices = [new() { Tag = tag, MultiLinePage = page }] };
        BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => Plan(args));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Contains(expected, failure.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_NoPage_OrAPageTwice_OrNoMacro_IsInvalid()
    {
        Assert.Contains("at least one", Violations(new() { Tag = Device }), StringComparison.Ordinal);
        Assert.Contains("given twice", Violations(new() { Tag = Device, MultiLinePage = Target, SingleLinePage = Target }), StringComparison.Ordinal);
        _schematics.Known[0].Macro = null;
        Assert.Contains("give 'macro'", Violations(new() { Tag = Device, MultiLinePage = Target }), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AGivenMacro_ReplacesTheOneFromThePart()
    {
        Directory.CreateDirectory(Path.Combine(_macroRoot, "Own"));
        File.WriteAllText(Path.Combine(_macroRoot, "Own", "2.ema"), string.Empty);
        PlcGenerateArgs args = Args();
        args.Devices![0].Macro = "Own/2.ema";
        Assert.EndsWith(@"$(MD_MACROS)\Own\2.ema", Assert.Single(Plan(args).Changes).NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Generates_CommitsOnce_AndReadsThePagesBack()
    {
        PlcGenerateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        PlcGenerateData data = Execute(args);
        Assert.Equal(Device, Assert.Single(Assert.Single(_schematics.Generated)).Tag);
        Assert.Equal(1, _schematics.Scope.Calls.Count(c => c == "Commit"));
        Assert.Equal(Target, Assert.Single(data.Pages).Name);
    }

    [Fact]
    public void Execute_NoPageGenerated_RollsBack()
    {
        _schematics.Result = [];
        PlcGenerateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => Execute(args));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (failure.Error.Reason, failure.Error.Execution));
        Assert.Equal(0, _schematics.Scope.Calls.Count(c => c == "Commit"));
    }

    [Fact]
    public void Execute_APageAddedSinceThePlan_IsStale()
    {
        PlcGenerateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _pages.Pages.Add(new PageSummary { Name = "=F+A1&EFS1/8" });
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_schematics.Generated);
    }

    private static PlcGenerateArgs Args() => new() { Project = "Line1", Devices = [new() { Tag = Device, MultiLinePage = Target }] };

    private string Violations(PlcGenerateDevice device) =>
        Assert.Throws<BridgeErrorException>(() => Plan(new PlcGenerateArgs { Project = "Line1", Devices = [device] })).Error.Metadata!["violations"];

    private WritePlan Plan(PlcGenerateArgs args) => (WritePlan)new PlcGeneratePlanOperation(_catalog, _pages, _schematics, _macros).Execute(args)!;

    private PlcGenerateData Execute(PlcGenerateArgs args) => (PlcGenerateData)new PlcGenerateExecuteOperation(_catalog, _pages, _schematics, _macros).Execute(args)!;

    private sealed class Schematics : IPlcSchematics
    {
        public FakeTransactions Scope { get; } = new();

        public List<PlcSchematicDevice> Known { get; } = [new() { Name = Device, Macro = @"$(MD_MACROS)\PXC\1.ema" }];

        public List<IReadOnlyList<PlcGenerateDevice>> Generated { get; } = [];

        public IReadOnlyList<string> Result { get; set; } = [Target];

        public IReadOnlyList<PlcSchematicDevice> Devices(string projectPath) => Known;

        public ITransactionScopeFactory Transactions(string projectPath) => Scope;

        public IReadOnlyList<string> Generate(string projectPath, IReadOnlyList<PlcGenerateDevice> devices)
        {
            Generated.Add(devices);
            return Result;
        }

        public IReadOnlyList<PlcGeneratedPage> ReadPages(string projectPath, IReadOnlyList<string> pages) => [.. pages.Select(p => new PlcGeneratedPage { Name = p })];
    }
}

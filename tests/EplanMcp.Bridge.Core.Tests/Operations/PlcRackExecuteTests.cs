using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// plc.rack.execute's order of work in one transaction (docs/tools/eplan_plc_rack.md, section 4) against a small fake
/// project: devices by tag, parts by device. Each insert places the macro's -K1 and its sub-device -K1-K1 on the page;
/// a rename to an existing tag joins the two devices, as EPLAN's NameService does (A122).
/// </summary>
[Trait("Size", "Small")]
public sealed class PlcRackExecuteTests : IDisposable
{
    private const string Rack = "=K1+UC1&EFS1/1";
    private const string Macro = "SIE/6ES7.ema";

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-plc-rack-run-").FullName;
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Dryer", Path = @"D:\Scratch\Dryer", Current = true });
    private readonly FakePageReader _pages = new();
    private readonly FakeMacroReader _macros;
    private readonly FakeMacroInserter _inserter = new(new FakeDeviceReader());
    private readonly Project _project;
    private readonly FakeTransactions _transactions = new();

    public PlcRackExecuteTests()
    {
        string file = Path.Combine(_root, Macro);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "macro");
        _macros = new FakeMacroReader(_root)
        {
            PartSource = (part, variant) => new PartMacroSource { Part = part, PartVariant = variant ?? "1", Macro = Macro },
            ReadSelection = (representation, variant) => new MacroContent
            {
                LayoutVerified = true,
                Representations = [new("Overview", [0]), new("MultiLine", [1, 5])],
                Width = 24,
                Height = 60,
            },
        };
        _project = new Project(_transactions);
        _inserter.Placed = _ => _project.Insert();
        foreach ((string name, string type) in new[] { (Rack, "Circuit"), ("=K1+UC1&EFA1/1", "Overview"), ("=K1+UC1&EFA1/2", "Overview") })
        {
            _inserter.Pages[name] = new MacroTargetPage { Name = name, Type = type };
            _pages.Pages.Add(new PageSummary { Name = name });
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void EachCard_IsOverviewRenamePartsThenPictureJoinedToIt()
    {
        PlcRackArgs args = Args();

        PlcRackData data = Execute(args);

        Assert.Equal(["=K1+UC1-KE1", "=K1+UC1-KE2"], data.Cards.Select(c => c.Tag));
        Assert.Equal(["=K1+UC1&EFA1/1", Rack, "=K1+UC1&EFA1/2", Rack], _inserter.Inserts.Select(i => i.Page));
        Assert.Equal(["SIE.6ES7131", "SIE.6ES7193-BU"], data.Cards[0].Parts);
        Assert.Equal(["=K1+UC1-KEC1"], data.Cards[0].SubDevices);
        // Overview and picture devices ended up as one device per card, and the sub-device as its own.
        Assert.Equal(["=K1+UC1-KE1", "=K1+UC1-KE2", "=K1+UC1-KE2-K1", "=K1+UC1-KEC1"], _project.Devices.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("SIE.6ES7193-6AR00", Assert.Single(_project.Parts[_project.Devices["=K1+UC1-KEC1"]]));
        Assert.Equal(2, _project.Transfers.Count);
        Assert.Equal(["Begin AI: eplan_plc_rack", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void APictureWithoutAMainFunction_IsStillJoinedToItsCard()
    {
        _project.PicturesHaveNoMain = true;

        PlcRackData data = Execute(Args());

        Assert.Equal(["=K1+UC1-KE1", "=K1+UC1-KE2"], data.Cards.Select(c => c.Tag));
        Assert.Equal(["=K1+UC1-KE1", "=K1+UC1-KE2", "=K1+UC1-KE2-K1", "=K1+UC1-KEC1"], _project.Devices.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void APartMissingAfterTheCommit_RollsTheWholeRackBack()
    {
        _project.DropPartsOnCommit = true;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args()));

        Assert.Equal(ErrorReason.EplanOperationFailed, error.Error.Reason);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void AChangedProject_IsPlanStale_AndNothingIsInserted()
    {
        PlcRackArgs args = Args();
        args.Fingerprint = "old";

        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_inserter.Inserts);
    }

    private PlcRackArgs Args()
    {
        var args = new PlcRackArgs
        {
            Project = "Dryer",
            RackPage = Rack,
            Cards =
            [
                new()
                {
                    Tag = "=K1+UC1-KE1", Part = "SIE.6ES7131", OverviewPage = "=K1+UC1&EFA1/1", PictureVariant = 5, ExtraParts = ["SIE.6ES7193-BU"],
                    SubDevices = [new() { MacroSuffix = "-K1", Tag = "=K1+UC1-KEC1", Part = "SIE.6ES7193-6AR00" }],
                },
                new() { Tag = "=K1+UC1-KE2", Part = "SIE.6ES7132", OverviewPage = "=K1+UC1&EFA1/2", PictureVariant = 5 },
            ],
        };
        args.Fingerprint = ((WritePlan)new PlcRackPlanOperation(_catalog, _pages, _macros, _inserter, _project).Execute(args)!).Fingerprint;
        return args;
    }

    private PlcRackData Execute(PlcRackArgs args) =>
        (PlcRackData)new PlcRackExecuteOperation(_catalog, _pages, _macros, _inserter, _project, _project).Execute(args)!;

    /// <summary>Devices by tag (one main function each), the parts on each device, the transfers after the commit.</summary>
    private sealed class Project(FakeTransactions transactions) : IDeviceRenamer, IDevicePartAssigner
    {
        private int _next;

        public Dictionary<string, string> Devices { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<string>> Parts { get; } = new(StringComparer.Ordinal);

        public List<string> Transfers { get; } = [];

        public bool DropPartsOnCommit { get; set; }

        /// <summary>The rack picture's box is no main function, as in the ET 200SP picture (live 2026-11-15).</summary>
        public bool PicturesHaveNoMain { get; set; }

        private int _inserts;

        public List<MacroFunctionRecord> Insert()
        {
            foreach (string tag in new[] { "=K1+UC1-K1", "=K1+UC1-K1-K1" })
            {
                Assert.False(Devices.ContainsKey(tag), "the previous card left the macro's tag in use");
                string id = (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Devices[tag] = id;
                Parts[id] = [];
            }

            bool main = !(PicturesHaveNoMain && ++_inserts % 2 == 0);
            return [new() { Name = "=K1+UC1-K1", Main = main, Category = "PLC" }, new() { Name = "=K1+UC1-K1-K1", Main = main, Category = "PLC" }];
        }

        public DeviceNames? ReadDevice(string projectPath, string tag) => null;

        public IReadOnlyList<string> FunctionNames(string projectPath) => [.. Devices.Keys];

        public RenameReadBack Rename(string projectPath, string tag, string newTag)
        {
            string id = Devices[tag];
            Devices.Remove(tag);
            if (Devices.TryGetValue(newTag, out string? existing))
            { Parts[existing].AddRange(Parts[id]); } // joined
            else
            { Devices[newTag] = id; }
            // EPLAN reads a device without a main function back as a connection point (live 2026-11-15).
            string read = PicturesHaveNoMain && _inserts % 2 == 0 && !tag.EndsWith("-K1-K1", StringComparison.Ordinal) ? newTag + ":L+" : newTag;
            return new RenameReadBack { MainName = read, VisibleName = newTag, Renamed = 1 };
        }

        public IReadOnlyList<DevicePartTarget> ReadMainFunctions(string projectPath, string tag, bool includeTerminals = false)
        {
            bool committed = transactions.Calls.Contains("Commit");
            return Devices.TryGetValue(tag, out string? id)
                ? [new DevicePartTarget { Id = id, Name = tag, Tag = tag, Parts = committed && DropPartsOnCommit ? [] : [.. Parts[id].Select(p => new DevicePart { PartNumber = p })] }]
                : [];
        }

        public void AddPart(string projectPath, string mainId, string part, string variant, int count = 1)
        {
            Assert.DoesNotContain("Commit", transactions.Calls);
            Parts[mainId].Add(part);
        }

        public ITransactionScopeFactory PartTransactions(string projectPath) => transactions;

        public void TransferPartData(string projectPath, string functionId) => Transfers.Add(functionId);

        public (int Count, string? Warning) AssignUnplacedFunctions(string projectPath, string tag) => (0, null);
    }
}

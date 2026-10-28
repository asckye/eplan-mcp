using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class ConnectionCreatePlanTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("connection-create-").FullName;
    private readonly ConnectionCreateFixture _fixture = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void DryRun_FileBackedT1Reader_PredictsFullPageEndpointsWithoutWriting()
    {
        string file = Path.Combine(_root, "symbols.json");
        File.WriteAllText(file, BridgeJson.FromObject(_fixture.Symbols).ToString());
        byte[] before = File.ReadAllBytes(file);
        var reader = new CatalogReader(file);
        JObject plan = ConnectionCreatePlanner.Plan(_fixture.Request.ToString(), _fixture.Scene, reader);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal([file], Directory.GetFiles(_root));
        Assert.Equal(1, reader.Reads);
        Assert.Equal("SPECIAL", reader.Search!.Library);
        Assert.Equal("complete_page_predicted_endpoints", (string?)plan["coverage"]);
        Assert.Equal([100d, 100d], plan["pageSize"]!.Values<double>());
        Assert.Equal("CO", (string?)plan["symbols"]![0]!["symbol"]!["name"]);
        Assert.Contains("2 undo steps", (string?)plan["generation"], StringComparison.Ordinal);
        string fingerprint = (string)plan["fingerprint"]!;
        _fixture.Symbols[0].Variant = 1;
        _fixture.Scene.SymbolBounds["CO/1"] = [-1, -1, 1, 1];
        File.WriteAllText(file, BridgeJson.FromObject(_fixture.Symbols).ToString());
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => ConnectionCreatePlanner.Plan(_fixture.Request.ToString(), _fixture.Scene, reader, fingerprint)).Error.Reason);
    }

    [Fact]
    public void DryRun_ExplicitTNode_PredictsAllThreePinEndpoints()
    {
        _fixture.Scene.Pins.Clear();
        _fixture.Scene.Page.Placements.Clear();
        JObject left = _fixture.AddPin(1, [8, 16], "Right", [4, 14, 8, 18]);
        JObject right = _fixture.AddPin(2, [40, 16], "Left", [40, 14, 44, 18]);
        _fixture.AddPin(3, [24, 40], "Down", [22, 40, 26, 44]);
        _fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("t_node", [24, 16], "Up", "Right", "Left"), ConnectionCreateFixture.Wire(left, right, [8, 16], [40, 16]));
        JObject plan = RoundTripPlan();
        Assert.Equal(("TLRO", 8), ((string?)plan["symbols"]![0]!["symbol"]!["name"], (int)plan["symbols"]![0]!["symbol"]!["variant"]!));
        Assert.Equal(["17/1/0", "17/2/0", "17/3/0"], plan["logicalAfterGenerate"]![0]!["pins"]!.Values<string>());
        Assert.Equal(3, ((JArray)plan["graphicalAutoconnect"]!).Count);
    }

    [Fact]
    public void DryRun_PotentialPoint_PredictsNullLogicalStartAndActualPinEnd()
    {
        _fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [24, 8], "Up"));
        JObject plan = RoundTripPlan();
        Assert.Equal("PCP", (string?)plan["symbols"]![0]!["symbol"]!["name"]);
        Assert.Equal(["17/2/0"], plan["logicalAfterGenerate"]![0]!["pins"]!.Values<string>());
        Assert.True((bool)plan["logicalAfterGenerate"]![0]!["potentialStartIsNull"]!);
        Assert.Single((JArray)plan["graphicalAutoconnect"]!);
    }

    [Fact]
    public void DryRun_PotentialPinOffsetChangesAfterFileRoundTrip_IsStale()
    {
        _fixture.Symbols[2].ConnectionPoints[0].Location = [0, 3];
        _fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [24, 8], "Up"));
        JObject plan = RoundTripPlan();
        Assert.Equal([24d, 5d], plan["symbols"]![0]!["position"]!.Values<double>());
        Assert.Equal([24d, 8d], plan["graphicalAutoconnect"]![0]![0]!.Values<double>());
        _fixture.Symbols[2].ConnectionPoints[0].Location = [0, 4];
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => _fixture.Plan((string)plan["fingerprint"]!));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (ex.Error.Reason, ex.Error.Execution));
    }

    [Theory]
    [InlineData("placement")]
    [InlineData("logical")]
    [InlineData("graphical")]
    [InlineData("pin")]
    [InlineData("grid")]
    [InlineData("symbol")]
    public void DryRun_FingerprintBindsCompletePageAndMasterData(string change)
    {
        string fingerprint = (string)RoundTripPlan()["fingerprint"]!;
        switch (change)
        {
            case "placement":
                _fixture.Scene.Page.Placements.Add(new() { Bounds = [80, 80, 84, 84], Properties = new() { ["text"] = "Unrelated page content" } });
                break;
            case "logical":
                _fixture.Scene.Logical.Add(new() { Page = ConnectionCreateFixture.PageName, StartDevice = "-K9", StartPin = "1" });
                break;
            case "graphical":
                _fixture.Scene.Graphical.Add([[80, 80], [84, 80]]);
                break;
            case "pin":
                _fixture.AddPin(9, [80, 80], "Right", [76, 78, 80, 82]);
                break;
            case "grid":
                _fixture.Scene.Page.GridSize = 2;
                break;
            case "symbol":
                _fixture.Symbols[0].Kind = "other";
                break;
        }
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => _fixture.Plan(fingerprint)).Error.Reason);
    }

    [Fact]
    public void DryRun_InventoryEnumerationOrder_DoesNotInvalidateFingerprint()
    {
        string fingerprint = (string)RoundTripPlan()["fingerprint"]!;
        _fixture.Scene.Page.Placements.Reverse();
        _fixture.Scene.Pins = _fixture.Scene.Pins.Reverse().ToDictionary();
        Assert.Equal(fingerprint, (string?)_fixture.Plan(fingerprint)["fingerprint"]);
    }

    [Fact]
    public void DryRun_FullOutlineCrossesPageEdge_RejectsEvenWhenInsertionPointFits()
    {
        _fixture.Scene.SymbolBounds["CO/0"] = [-1, -1, 80, 1];
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => RoundTripPlan()).Error.Reason);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("page_size")]
    [InlineData("placement_bounds")]
    [InlineData("symbol_bounds")]
    [InlineData("page_type")]
    [InlineData("missing_symbol")]
    [InlineData("ambiguous_symbol")]
    [InlineData("unknown_offset")]
    public void DryRun_UnknownCoverageOrUnsupportedVariant_FailsClosed(string missing)
    {
        switch (missing)
        {
            case "incomplete":
                _fixture.Scene.Complete = false;
                break;
            case "page_size":
                _fixture.Scene.Page.PageSize = null;
                _fixture.Scene.Page.PlotFrameSize = [1000, 1000];
                break;
            case "placement_bounds":
                _fixture.Scene.Page.Placements[0].Bounds = null;
                break;
            case "symbol_bounds":
                _fixture.Scene.SymbolBounds.Clear();
                break;
            case "page_type":
                _fixture.Scene.PageType = "Overview";
                break;
            case "missing_symbol":
                _fixture.Symbols.Clear();
                break;
            case "ambiguous_symbol":
                _fixture.Symbols.Add(_fixture.Symbols[0]);
                break;
            case "unknown_offset":
                _fixture.Symbols[0].ConnectionPoints[0].Location = [4, 0];
                break;
        }
        Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), Outcome(() => RoundTripPlan()));
    }

    [Fact]
    public void DryRun_ExistingGraphicalLineOrPlacementConflict_Rejects()
    {
        _fixture.Scene.Graphical.Add([[12, 0], [12, 16]]);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => RoundTripPlan()).Error.Reason);
        _fixture.Scene.Graphical.Clear();
        _fixture.Scene.Page.Placements.Add(new() { Bounds = [22, 6, 26, 10] });
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => RoundTripPlan()).Error.Reason);
    }

    [Fact]
    public void DryRun_LocatorDigestChanged_IsStaleAndNeverSubstitutesPin()
    {
        _fixture.Scene.Page.Placements[0].Locator.Digest = new string('b', 64);
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), Outcome(() => RoundTripPlan()));
    }

    private JObject RoundTripPlan()
    {
        string file = Path.Combine(_root, "request.json");
        File.WriteAllText(file, _fixture.Request.ToString());
        return ConnectionCreatePlanner.Plan(File.ReadAllText(file), _fixture.Scene, _fixture);
    }

    private static (string, string?) Outcome(Action call)
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(call);
        return (ex.Error.Reason, ex.Error.Execution);
    }

    private sealed class CatalogReader(string file) : ISymbolReader
    {
        internal int Reads { get; private set; }
        internal SymbolSearchArgs? Search { get; private set; }
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args)
        {
            Reads++;
            Search = args;
            return JArray.Parse(File.ReadAllText(file)).ToObject<List<SymbolSearchItem>>()!;
        }
    }
}

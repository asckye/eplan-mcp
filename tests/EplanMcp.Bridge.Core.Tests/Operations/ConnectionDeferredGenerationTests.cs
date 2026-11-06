using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionDeferredGenerationTests
{
    [Fact]
    public void LiveTNodeInput_Deferred_SkipsConflictingLinesAndLogicalEndpoints()
    {
        var creator = new Creator();
        // Redraw report call 926; only the project identity is replaced by the isolated test fixture.
        JObject input = JObject.Parse("""
            {"project":"Line1","page":"=GAA+A1&EFS1/1","snap_to_grid":false,"dry_run":false,
             "layout_guard":"warn","response_format":"detailed","generate":false,
             "items":[{"kind":"t_node","position":[96,248],"directions":["Up","Down","Right"],"variant":1}]}
            """);
        creator.Fixture.Request.RemoveAll();
        foreach (JProperty property in input.Properties())
        { creator.Fixture.Request[property.Name] = property.Value; }
        creator.Fixture.Scene.Page.Page = "=GAA+A1&EFS1/1";
        creator.Fixture.Scene.Page.PageSize = [420, 300];
        creator.Fixture.Symbols.Add(ConnectionCreateFixture.Variant("TOUR", "TNodeRight", 1, "Up", "Down", "Right"));
        creator.Fixture.Scene.Graphical = [[[96, 200], [96, 280]], [[80, 248], [120, 248]]];
        creator.Fixture.Scene.Pins["17/1/0"].Direction = "Unknown";
        JObject result = creator.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Equal("placement_only", (string?)result["coverage"]);
        Assert.Single(result["actualPlacements"]!);
        Assert.Empty(result["graphicalAutoconnect"]!);
        Assert.Empty(result["logicalAfterGenerate"]!);
        Assert.False(result.ContainsKey("actualEndpoints"));
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "CloseUndo"));
    }

    [Fact]
    public void AutoconnectBreak_Deferred_PlacesTheInvisibleBreakPoint()
    {
        var creator = new Creator();
        creator.Fixture.Symbols.Add(ConnectionCreateFixture.Variant("ACBP", "ConnectionBreakpoint", 0, "Left", "Down", "Right", "Up"));
        creator.Fixture.Request["generate"] = false;
        creator.Fixture.Request["items"] = new JArray(JObject.Parse("""{"kind":"autoconnect_break","position":[64,64]}"""));

        JObject result = creator.Execute();

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal("placement_only", (string?)result["coverage"]);
        Assert.Single(result["actualPlacements"]!);
    }

    [Fact]
    public void AutoconnectBreak_WithGeneration_IsRefused()
    {
        var creator = new Creator();
        creator.Fixture.Request["items"] = new JArray(JObject.Parse("""{"kind":"autoconnect_break","position":[64,64]}"""));

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => creator.Execute());

        Assert.Contains("generate:false", error.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void DefaultOrExplicitGeneration_StillRejectsConflictingAutoconnect(bool? generate)
    {
        var creator = new Creator();
        creator.Fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("corner", [64, 64], "Left", "Up"));
        creator.Fixture.Scene.Graphical = [[[40, 64], [80, 64]]];
        if (generate.HasValue)
        { creator.Fixture.Request["generate"] = generate.Value; }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("existing autoconnect", error.Message, StringComparison.Ordinal);
        Assert.Empty(creator.Transactions.Calls);
    }

    [Theory]
    [InlineData("corner")]
    [InlineData("t_node")]
    [InlineData("potential_point")]
    [InlineData("interruption_point")]
    [InlineData("potential_definition")]
    public void Deferred_AllPlacementKinds_ReadBackInOneUndoStep(string kind)
    {
        var creator = new Creator();
        creator.Fixture.Symbols.Add(ConnectionCreateFixture.Variant("BP", "InterruptionPoint", 0, "Up"));
        creator.Fixture.Request["generate"] = false;
        creator.Fixture.Request["items"] = new JArray(kind switch
        {
            "corner" => ConnectionCreateFixture.Node(kind, [64, 64], "Left", "Up"),
            "t_node" => ConnectionCreateFixture.Node(kind, [64, 64], "Left", "Right", "Up"),
            "potential_point" => ConnectionCreateFixture.Node(kind, [64, 64], "Up"),
            "interruption_point" => JObject.Parse("""{"kind":"interruption_point","position":[64,64],"direction":"Up","name":"-BP1","counterpart_pages":["not-placed-yet"]}"""),
            _ => JObject.Parse("""{"kind":"potential_definition","position":[64,64],"potential_name":"N"}""")
        });
        JObject result = creator.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Single(result["actualPlacements"]!);
        Assert.False(result.ContainsKey("interruptionScope"));
    }

    /// <summary>PDF redraw 2026-11-06: =GAA-2L1 on =GAA+A1 shows -2L1; a location in the tag makes EPLAN show the full name.</summary>
    [Theory]
    [InlineData("24V", "=F-24V")]
    [InlineData("-24V", "=F-24V")]
    [InlineData("=G+B2-24V", "=G+B2-24V")]
    [InlineData("+B2-24V", "+B2-24V")]
    public void InterruptionName_ShortFormsTakeTheHigherLevelFunction(string requested, string planned)
    {
        var creator = new Creator();
        creator.Fixture.Symbols.Add(ConnectionCreateFixture.Variant("BP", "InterruptionPoint", 0, "Up"));
        creator.Fixture.Request["generate"] = false;
        creator.Fixture.Request["items"] = new JArray(new JObject { ["kind"] = "interruption_point", ["position"] = new JArray(64, 64), ["direction"] = "Up", ["name"] = requested });
        Assert.Equal(planned, (string?)creator.Execute()["symbols"]![0]!["interruption"]!["name"]);
    }

    [Theory]
    [InlineData("wire")]
    [InlineData("definition_point")]
    [InlineData("cable_definition")]
    [InlineData("unknown")]
    public void Deferred_OtherKinds_AreRejectedBeforeWriting(string kind)
    {
        var creator = new Creator();
        creator.Fixture.Request["generate"] = false;
        creator.Fixture.Request["items"] = new JArray(new JObject { ["kind"] = kind });
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(creator.Transactions.Calls);
    }

    [Fact]
    public void Deferred_MissingCommittedNode_RollsBackTheSingleStep()
    {
        var creator = new Creator { MissingNode = true };
        creator.Fixture.Request["generate"] = false;
        creator.Fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("corner", [64, 64], "Left", "Up"));
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.DoesNotContain("CloseUndo", creator.Transactions.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("\"false\"")]
    public void Generate_MustBeBoolean(string value)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request["generate"] = JToken.Parse(value);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error.Reason);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("variant")]
    [InlineData("same_position_replacement")]
    public void Deferred_CommittedSymbolMustMatchSavedIdentityAndVariant(string mismatch)
    {
        var creator = new Creator { Mismatch = mismatch };
        creator.Fixture.Request["generate"] = false;
        creator.Fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("corner", [64, 64], "Left", "Up"));
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(1, creator.Readbacks);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.DoesNotContain("CloseUndo", creator.Transactions.Calls);
    }

    private sealed class Creator : IConnectionCreator, IConnectionGenerator, IConnectionDefinitionPointWriter, IConnectionPlacementReadback
    {
        internal ConnectionCreateFixture Fixture { get; } = new();
        internal FakeTransactions Transactions { get; } = new();
        private readonly List<DrawingPlacement> _created = [];
        private readonly List<(long Oid, string Kind, string Variant)> _identities = [];
        private bool _written;
        internal bool MissingNode { get; init; }
        internal string? Mismatch { get; init; }
        internal int Readbacks { get; private set; }
        internal Creator() => Transactions.Snapshot = () => { bool old = _written; return () => _written = old; };
        internal JObject Execute() => Assert.IsType<JObject>(new OperationContext("deferred", true, null).Run(() =>
            new ConnectionCreateExecuteOperation(this, this, Transactions).Execute(new JObject
            { ["request"] = Fixture.Request, ["fingerprint"] = Fixture.Plan()["fingerprint"] })));
        public ConnectionCreatePlanner.Scene Read(string project, string page) => !_written ? Fixture.Scene : new()
        {
            Project = Fixture.Scene.Project,
            Layout = Fixture.Scene.Layout,
            Page = new()
            {
                Project = Fixture.Scene.Page.Project,
                Page = Fixture.Scene.Page.Page,
                GridSize = 4,
                PageSize = Fixture.Scene.Page.PageSize,
                Placements = [.. Fixture.Scene.Page.Placements, .. MissingNode ? [] : _created]
            },
            Graphical = [[[12, 20], [60, 20]]]
        };
        public LayoutGuard.Model Measure(string project, string page) => LayoutGuard.From(Read(project, page));
        public List<SymbolSearchItem> Read(string project, SymbolSearchArgs args) => Fixture.Read(project, args);
        public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null) => () =>
        {
            _written = true;
            foreach (JToken symbol in symbols)
            {
                string kind = symbol["interruption"] is not null ? "InterruptionPoint" : (string?)symbol["symbol"]!["name"] == "PCP" ? "PotentialDefinition" : "SymbolReference";
                var row = new DrawingPlacement
                {
                    Locator = new() { Type = 17, Oid = 900 + _created.Count },
                    Kind = kind,
                    Location = symbol["position"]!.Values<double>().ToArray(),
                    Properties = new() { ["variant"] = symbol["symbol"]!["variant"]!.ToString() }
                };
                _created.Add(row);
                _identities.Add((row.Locator.Oid, kind, row.Properties["variant"]!));
                if (Mismatch == "kind")
                { row.Kind = "Line"; }
                if (Mismatch == "variant")
                { row.Properties["variant"] = "999"; }
                if (Mismatch == "same_position_replacement")
                { row.Locator.Oid += 100; }
            }
            verifyLayout?.Invoke(Measure(project, page));
        };
        public Action PrepareDefinitionPoints(string project, string page, JArray planned) => () =>
        {
            _written = true;
            foreach (JToken point in planned)
            { _created.Add(new() { Locator = new() { Type = 17, Oid = 900 + _created.Count }, Kind = "PotentialDefinition", Location = point["position"]!.Values<double>().ToArray() }); }
        };
        public void VerifyCreatedSymbols(string project, string page, JArray symbols)
        {
            Assert.Contains("Commit", Transactions.Calls);
            Readbacks++;
            foreach (var id in _identities)
            {
                DrawingPlacement? row = Read(project, page).Page.Placements.SingleOrDefault(p => p.Locator.Oid == id.Oid);
                if (row is null || row.Kind != id.Kind || row.Properties.GetValueOrDefault("variant") != id.Variant)
                { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Saved symbol identity, kind or variant changed after commit."); }
            }
        }
        public JArray ReadDefinitionPoints(string project, string page, JArray planned) => (JArray)planned.DeepClone();
        public void AddDefinitionArticles(string project, string page, JArray planned) => throw new NotSupportedException();
        public void WriteDefinitionSources(string project, string page, JArray planned) => throw new NotSupportedException();
        public List<string?[]> Endpoints(string project, string page) => throw new InvalidOperationException("Deferred placement must not inspect endpoints.");
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => throw new InvalidOperationException("Deferred placement must not inspect pairing.");
        public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false) => Transactions;
        public void Generate(string projectPath, IReadOnlyList<string> pages) => throw new InvalidOperationException("Deferred placement must not generate connections.");
        public void Undo(string project) => _written = false;
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
    }
}

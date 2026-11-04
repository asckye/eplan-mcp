using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionDefinitionPointTests
{
    [Theory]
    [InlineData("{}", "definition_point")]
    [InlineData("null", "definition_point")]
    [InlineData("{\"color\":\"\"}", "color")]
    [InlineData("{\"color\":\"  \"}", "color")]
    [InlineData("{\"color\":7}", "color")]
    [InlineData("{\"designation\":null}", "designation")]
    [InlineData("{\"cross_section\":\"abc\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"NaN\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"Infinity\"}", "cross_section")]
    [InlineData("{\"cross_section\":\"1e999\"}", "cross_section")]
    [InlineData("{\"cross_section\":1.5}", "cross_section")]
    [InlineData("{\"color\":\"RD\",\"symbol\":\"PDP\"}", "symbol")]
    [InlineData("{\"color\":\"RD\",\"symbol_variant\":9}", "symbol_variant")]
    [InlineData("{\"color\":\"RD\",\"symbol_variant\":\"0\"}", "symbol_variant")]
    [InlineData("{\"color\":\"RD\",\"position\":[16,12]}", "position")]
    [InlineData("{\"color\":\"RD\",\"position\":[16]}", "position")]
    [InlineData("{\"color\":\"RD\",\"unknown\":1}", "unknown")]
    public void InvalidDefinitionPoint_IsRejectedBeforeWriting(string definition, string field)
    {
        ConnectionCreateFixture fixture = Fixture();
        fixture.Request["items"]![0]!["definition_point"] = JToken.Parse(definition);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("/items/0/definition_point" + (field == "definition_point" ? "" : "/" + field), error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.5")]
    [InlineData("1e2")]
    [InlineData(" 1.5 ")]
    [InlineData("+1.5")]
    [InlineData(".5")]
    public void CrossSection_FiniteNumericText_IsPreserved(string value)
    {
        var fixture = Fixture();
        fixture.Request["items"]![0]!["definition_point"]!["cross_section"] = value;
        Assert.Equal(value, (string?)Assert.Single(fixture.Plan()["definitionPoints"]!)["properties"]!["cross_section"]);
    }

    [Theory]
    [InlineData("CDP", 0)]
    [InlineData("CDPNG", 0)]
    [InlineData("CDP", 2)]
    public void Preview_UsesActualSymbolVariantAndFirstSegmentMidpoint(string symbol, int variant)
    {
        var fixture = Fixture();
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDP", "ConnectionDefinitionPoint", 2));
        fixture.Request["items"]![0]!["definition_point"]!["symbol"] = symbol;
        fixture.Request["items"]![0]!["definition_point"]!["symbol_variant"] = variant;
        JObject plan = fixture.Plan();
        JToken point = Assert.Single(plan["definitionPoints"]!);
        Assert.Equal([16d, 8d], point["position"]!.Values<double>());
        Assert.Equal((symbol, variant), ((string?)point["symbol"]!["name"], (int)point["symbol"]!["variant"]!));
        Assert.Equal("RD", (string?)point["properties"]!["color"]);
        Assert.Equal("1.5", (string?)point["properties"]!["cross_section"]);
        Assert.Equal(1, (int)plan["undoSteps"]!);
        Assert.Empty(fixture.Scene.PointRoutes);
        Assert.Equal((string?)plan["fingerprint"], (string?)fixture.Plan()["fingerprint"]);
    }

    [Theory]
    [InlineData(24, 12)]
    [InlineData(14.5, 8)]
    public void ExplicitPosition_OnAnySegment_IsPreserved(double x, double y)
    {
        var fixture = Fixture();
        fixture.Request["items"]![0]!["definition_point"]!["position"] = new JArray(x, y);
        Assert.Equal([x, y], Assert.Single(fixture.Plan()["definitionPoints"]!)["position"]!.Values<double>());
    }

    [Fact]
    public void DefaultMidpoint_OnOffGridPinLine_RemainsOnTheWire()
    {
        var fixture = Straight(offGrid: true);
        JToken point = Assert.Single(fixture.Plan()["definitionPoints"]!);
        Assert.Equal([10d, 24d], point["position"]!.Values<double>());
    }

    [Fact]
    public void DefinitionPointSymbolEvidenceChanges_MakesPlanStale()
    {
        var fixture = Fixture();
        string fingerprint = (string)fixture.Plan()["fingerprint"]!;
        fixture.Request["items"]![0]!["definition_point"]!["symbol"] = "CDPNG";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => fixture.Plan(fingerprint)).Error.Reason);
    }

    [Fact]
    public void DefinitionPoints_CannotBeSilentlyIgnoredByCableBranch()
    {
        var fixture = Fixture();
        ((JArray)fixture.Request["items"]!).Add(new JObject { ["kind"] = "cable_definition" });
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("separately", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StraightWireWithDefinitionPoint_GeneratesAfterTheCommit_InOneUndoStep()
    {
        var creator = new Creator();
        JObject result = creator.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Equal(["place", "define", "generate", "readback"], creator.Events);
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal(1, creator.Transactions.Calls.Count(c => c == "CloseUndo"));
        Assert.Equal("RD", (string?)Assert.Single(result["definitionPoints"]!)["connectionValues"]!["color"]);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("symbol")]
    [InlineData("position")]
    [InlineData("endpoints")]
    [InlineData("properties/color")]
    [InlineData("properties/cross_section")]
    [InlineData("properties/designation")]
    [InlineData("connectionValues/color")]
    [InlineData("connectionValues/cross_section")]
    [InlineData("connectionValues/designation")]
    public void ReadbackMismatch_RollsBackAndReportsTheExactField(string field)
    {
        var creator = new Creator { Mismatch = field };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("definitionPoints/0/" + field, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, creator.State);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.DoesNotContain("CloseUndo", creator.Transactions.Calls);
    }

    [Fact]
    public void GenerationFailure_RollsBackTheCommittedPlacement()
    {
        var creator = new Creator { Mismatch = "generate" };
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error.Execution);
        Assert.Contains("Commit", creator.Transactions.Calls);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.Equal(0, creator.State);
    }

    [Fact]
    public void MissingCreatedPoint_RollsBackTheCommittedWrite()
    {
        var creator = new Creator { Mismatch = "missing" };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => creator.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("definitionPoints/count", error.Message, StringComparison.Ordinal);
        Assert.Contains("Commit", creator.Transactions.Calls);
        Assert.Contains("Rollback", creator.Transactions.Calls);
        Assert.Equal(0, creator.State);
    }

    [Fact]
    public void Layout_UsesInsertionPointRatherThanOffsetOutlineCentreForOwningWire()
    {
        var point = LayoutGuard.Proposed("ConnectionDefinitionPoint", [16, 8], [14, 7, 20, 13]);
        var model = new LayoutGuard.Model { Page = new() { GridSize = 4, PageSize = [100, 100] }, Config = new() { MarginMm = 0 } };
        model.PointRoutes[$"0/{point.Locator.Oid}"] = [[[8, 8], [24, 8]]];
        model.Lines = [new() { Points = [[8, 8], [24, 8]], Changed = true }];
        Assert.DoesNotContain(LayoutGuard.Check(model, [point], suggest: false), v => v.Rule == "R4");
        model.Lines = [new() { Points = [[18, 4], [18, 20]], Changed = true }];
        Assert.Contains(LayoutGuard.Check(model, [point], suggest: false), v => v.Rule == "R4");
    }

    private static ConnectionCreateFixture Fixture()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDP", "ConnectionDefinitionPoint", 0));
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CDPNG", "ConnectionDefinitionPoint", 0));
        fixture.Request["items"]![0]!["definition_point"] = new JObject { ["color"] = "RD", ["cross_section"] = "1.5" };
        return fixture;
    }
    private static ConnectionCreateFixture Straight(bool offGrid = false)
    {
        var fixture = Fixture();
        fixture.Scene.Pins.Clear();
        fixture.Scene.Page.Placements.Clear();
        double x = offGrid ? 10 : 8;
        JObject from = fixture.AddPin(1, [x, 8], "Up", [x - 2, 4, x + 2, 8]);
        JObject to = fixture.AddPin(2, [x, 40], "Down", [x - 2, 40, x + 2, 44]);
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Wire(from, to, [x, 8], [x, 40]));
        fixture.Request["items"]![0]!["definition_point"] = new JObject { ["color"] = "RD", ["cross_section"] = "1.5", ["designation"] = "W1" };
        return fixture;
    }
    private sealed class Creator : IConnectionCreator, IConnectionGenerator, IConnectionDefinitionPointWriter
    {
        public ConnectionCreateFixture Fixture { get; } = Straight();
        public FakeTransactions Transactions { get; } = new();
        public List<string> Events { get; } = [];
        public string? Mismatch { get; init; }
        public int State { get; private set; }
        public Creator() { Transactions.Snapshot = () => { int old = State; return () => State = old; }; }
        public JObject Execute() => Assert.IsType<JObject>(new OperationContext("definition-test", true, null).Run(() => new ConnectionCreateExecuteOperation(this, this, Transactions)
            .Execute(new JObject { ["request"] = Fixture.Request, ["fingerprint"] = Fixture.Plan()["fingerprint"] })));
        public ConnectionCreatePlanner.Scene Read(string project, string page) => State == 0 ? Fixture.Scene : new()
        { Page = Fixture.Scene.Page, Layout = Fixture.Scene.Layout, Pins = Fixture.Scene.Pins, Graphical = Fixture.Plan()["graphicalAutoconnect"]!.ToObject<List<double[][]>>()! };
        public List<SymbolSearchItem> Read(string project, SymbolSearchArgs args) => Fixture.Read(project, args);
        public LayoutGuard.Model Measure(string project, string page) => LayoutGuard.From(Read(project, page));
        public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null) => () => { Events.Add("place"); State = 1; };
        public Action PrepareDefinitionPoints(string project, string page, JArray planned) => () => { Assert.Equal(1, State); Events.Add("define"); State = 2; };
        public void Generate(string project, IReadOnlyList<string> pages)
        {
            Assert.Equal(2, State);
            Assert.Contains("Commit", Transactions.Calls); // A52: generation only sees committed placement.
            Events.Add("generate");
            if (Mismatch == "generate")
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "generation failed"); }
        }
        public JArray ReadDefinitionPoints(string project, string page, JArray planned)
        {
            Assert.Contains("Commit", Transactions.Calls);
            Events.Add("readback");
            var rows = (JArray)planned.DeepClone();
            foreach (JToken row in rows)
            { row["connectionValues"] = row["properties"]!.DeepClone(); }
            if (Mismatch == "missing")
            { rows.Clear(); return rows; }
            if (Mismatch is { } field)
            {
                string[] path = field.Split('/');
                JToken owner = path.Length == 1 ? rows[0] : rows[0]![path[0]]!;
                owner[path[^1]] = field == "endpoints" ? new JArray("other", "network") : field == "position" ? new JArray(99d, 99d) : new JValue("wrong");
            }
            return rows;
        }
        public List<string?[]> Endpoints(string project, string page) => [["17/2/0", "17/1/0"]];
        public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false) { Assert.True(undoCommitted); return Transactions; }
        public void Undo(string project) => throw new NotSupportedException();
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
    }
}

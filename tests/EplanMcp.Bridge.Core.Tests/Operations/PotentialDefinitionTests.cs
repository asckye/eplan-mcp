using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PotentialDefinitionTests
{
    [Theory]
    [InlineData("potential_name")]
    [InlineData("signal_name")]
    public void NamesAndPosition_ArePreserved_WithoutLogicalEndpoints(string name)
    {
        var f = Fixture();
        f.Request["items"]![0]![name] = "N";
        JObject plan = f.Plan();
        Assert.Equal("N", (string?)plan["definitionPoints"]![0]!["properties"]![name]);
        Assert.Equal([40d, 40d], plan["definitionPoints"]![0]!["position"]!.Values<double>());
        Assert.Empty(plan["logicalAfterGenerate"]!);
        Assert.Equal(1, (int)plan["undoSteps"]!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"potential_name\":null}")]
    [InlineData("{\"signal_name\":\" \"}")]
    [InlineData("{\"potential_name\":1}")]
    [InlineData("{\"potential_name\":\"N\",\"unexpected\":true}")]
    public void InvalidNames_AreRejectedBeforeWriting(string fields)
    {
        var f = Fixture();
        var row = (JObject)f.Request["items"]![0]!;
        foreach (JProperty property in JObject.Parse(fields).Properties())
        { row[property.Name] = property.Value; }
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => f.Plan()).Error.Reason);
    }

    [Theory]
    [InlineData("potential_value", "null")]
    [InlineData("potential_value", "3")]
    [InlineData("color", "true")]
    [InlineData("cross_section", "\"wide\"")]
    [InlineData("potential_type", "\"2\"")]
    [InlineData("potential_type", "2.5")]
    [InlineData("potential_type", "2147483648")]
    [InlineData("line_color", "\"5\"")]
    [InlineData("line_color", "5.5")]
    [InlineData("line_color", "2147483648")]
    public void EngineeringFields_RejectInvalidTypesBeforeWriting(string field, string json)
    {
        var f = Fixture();
        f.Request["items"]![0]!["potential_name"] = "N";
        f.Request["items"]![0]![field] = JToken.Parse(json);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => f.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/items/0/" + field, error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1,5")]
    public void EngineeringFields_PreserveTypedValuesAndStoreCommaCrossSection(string section)
    {
        var f = Fixture();
        JObject row = (JObject)f.Request["items"]![0]!;
        row["potential_name"] = "PE";
        row["potential_value"] = "PE";
        row["potential_type"] = 4;
        row["line_color"] = 5;
        row["color"] = "GNYE";
        row["cross_section"] = section;
        JToken properties = f.Plan()["definitionPoints"]![0]!["properties"]!;
        Assert.Equal("PE", (string?)properties["potential_value"]);
        Assert.Equal(JTokenType.Integer, properties["potential_type"]!.Type);
        Assert.Equal(4, (int)properties["potential_type"]!);
        Assert.Equal(JTokenType.Integer, properties["line_color"]!.Type);
        Assert.Equal(5, (int)properties["line_color"]!);
        Assert.Equal("GNYE", (string?)properties["color"]);
        Assert.Equal("1,5", (string?)properties["cross_section"]);
    }

    [Fact]
    public void EmbeddedMacro_ContainsNoMachineOrExampleProjectData()
    {
        using Stream resource = typeof(PotentialDefinitionTests).Assembly.GetManifestResourceStream("potential-definition.ema")!;
        Assert.NotNull(resource);
        using var reader = new StreamReader(resource);
        string text = reader.ReadToEnd();
        foreach (string forbidden in new[] { "Users", "SourceProject=\"C", "EPL_DEMO" })
        { Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase); }
    }

    [Fact]
    public void ExistingConnection_KeepsEndpointsAndSkipsGeneration()
    {
        var creator = new Creator(existing: true);
        JObject result = creator.Execute();
        Assert.Single(result["actualEndpoints"]!);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
    }

    [Theory]
    [InlineData("[-1,40]")]
    [InlineData("[10001,40]")]
    [InlineData("[40]")]
    public void InvalidPosition_IsRejectedBeforeWriting(string position)
    {
        var f = Fixture();
        f.Request["items"]![0]!["potential_name"] = "N";
        f.Request["items"]![0]!["position"] = JArray.Parse(position);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => f.Plan()).Error.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("position")]
    [InlineData("type")]
    [InlineData("properties/potential_name")]
    [InlineData("properties/signal_name")]
    [InlineData("properties/potential_value")]
    [InlineData("properties/potential_type")]
    [InlineData("properties/color")]
    [InlineData("properties/cross_section")]
    public void Execute_OneTransaction_NoGeneration_AndMismatchRollsBack(string? mismatch)
    {
        var creator = new Creator { Mismatch = mismatch };
        if (mismatch is not null)
        { Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(creator.Execute).Error.Execution); Assert.Contains("Rollback", creator.Transactions.Calls); }
        else
        { Assert.Equal(ExecutionState.Committed, (string?)creator.Execute()["execution"]); Assert.Single(creator.Transactions.Calls, c => c == "CloseUndo"); }
    }

    private static ConnectionCreateFixture Fixture()
    {
        var f = new ConnectionCreateFixture();
        f.Request["items"] = new JArray(new JObject { ["kind"] = "potential_definition", ["position"] = new JArray(40, 40) });
        return f;
    }
    private sealed class Creator : IConnectionCreator, IConnectionGenerator, IConnectionDefinitionPointWriter
    {
        private readonly ConnectionCreateFixture _fixture = Fixture();
        public FakeTransactions Transactions { get; } = new();
        public string? Mismatch { get; init; }
        public Creator(bool existing = false)
        {
            _fixture.Request["items"]![0]!["potential_name"] = "N";
            _fixture.Request["items"]![0]!["signal_name"] = "S";
            _fixture.Request["items"]![0]!["potential_value"] = "N";
            _fixture.Request["items"]![0]!["potential_type"] = 2;
            _fixture.Request["items"]![0]!["color"] = "BU";
            _fixture.Request["items"]![0]!["cross_section"] = "1.5";
            if (existing)
            {
                _fixture.Scene.Page.Placements.Clear();
                _fixture.Scene.Pins.Clear();
                _fixture.AddPin(1, [40, 20], "Up", [38, 16, 42, 20]);
                _fixture.AddPin(2, [40, 60], "Down", [38, 60, 42, 64]);
                _fixture.Scene.Graphical = [[[40, 20], [40, 60]]];
                _fixture.Scene.Logical = [new() { StartDevice = "17/1/0", EndDevice = "17/2/0" }];
            }
        }
        public JObject Execute() => Assert.IsType<JObject>(new OperationContext("potential", true, null).Run(() => new ConnectionCreateExecuteOperation(this, this, Transactions).Execute(new JObject { ["request"] = _fixture.Request, ["fingerprint"] = _fixture.Plan()["fingerprint"] })));
        public ConnectionCreatePlanner.Scene Read(string project, string page) => _fixture.Scene;
        public List<SymbolSearchItem> Read(string project, SymbolSearchArgs args) => _fixture.Read(project, args);
        public LayoutGuard.Model Measure(string project, string page) => LayoutGuard.From(_fixture.Scene);
        public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null) => () => { };
        public Action PrepareDefinitionPoints(string project, string page, JArray planned) => () => { };
        public void AddDefinitionArticles(string project, string page, JArray planned) { }
        public void WriteDefinitionSources(string project, string page, JArray planned) { }
        public JArray ReadDefinitionPoints(string project, string page, JArray planned)
        {
            var rows = (JArray)planned.DeepClone();
            if (Mismatch is { } path)
            { string[] parts = path.Split('/'); (parts.Length == 1 ? rows[0]! : rows[0]![parts[0]]!)[parts[^1]] = "mismatch"; }
            return rows;
        }
        public List<string?[]> Endpoints(string project, string page) => _fixture.Scene.Logical.Select(c => new[] { c.StartDevice, c.EndDevice }).ToList();
        public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false) => Transactions;
        public void Generate(string project, IReadOnlyList<string> pages, bool rebuild = false) => throw new InvalidOperationException("A standalone potential must not generate connections.");
        public void Undo(string project) => throw new NotSupportedException();
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
    }
}

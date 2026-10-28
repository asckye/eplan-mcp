using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class CableDefinitionPlanTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cable-plan-").FullName;
    private readonly CableDefinitionFixture _fixture = new();
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FullImpact_ExistingAndSameCallWires_PreservesEveryExplicitMappingAndPriorState()
    {
        var wire = _fixture.AddExisting();
        _fixture.Cable["points"] = JArray.Parse("[[16,16],[48,16]]");
        _fixture.Cores.Add(CableDefinitionFixture.Core(CableDefinitionFixture.Reference(wire), "PE", "GNYE"));
        string file = SaveInventory();
        byte[] before = File.ReadAllBytes(file);
        var creator = new Creator(_fixture);
        WritePlan plan = Assert.IsType<WritePlan>(new ConnectionCreatePlanOperation(creator).Execute(_fixture.Base.Request));
        JObject preview = JObject.Parse(plan.Details!["preview"]);
        var rows = (JArray)preview["coreImpact"]!;
        Assert.Equal(2, rows.Count);
        Assert.Equal(("-W3", "1", "BK"), ((string?)rows[0]!["designation"], (string?)rows[0]!["label"], (string?)rows[0]!["colour"]));
        Assert.Equal(JTokenType.Null, rows[0]!["before"]!.Type);
        Assert.Equal(("-W3", "PE", "GNYE"), ((string?)rows[1]!["designation"], (string?)rows[1]!["label"], (string?)rows[1]!["colour"]));
        Assert.True(JToken.DeepEquals(CableDefinitionFixture.Reference(wire), rows[1]!["connection"]));
        Assert.Equal(("7", "BU", "1,5"), ((string?)rows[1]!["before"]!["label"], (string?)rows[1]!["before"]!["colour"], (string?)rows[1]!["before"]!["crossSection"]));
        Assert.Equal(1.5, (double)rows[1]!["crossSectionMm2"]!);
        Assert.Contains("coreImpact", Assert.Single(plan.Changes).NewValue, StringComparison.Ordinal);
        Assert.DoesNotContain("2 undo steps", Assert.Single(plan.Changes).NewValue, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal([file], Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("colour")]
    [InlineData("label")]
    [InlineData("crossSection")]
    [InlineData("cable")]
    [InlineData("digest")]
    [InlineData("endpoint")]
    [InlineData("geometry")]
    [InlineData("newName")]
    [InlineData("incomplete")]
    public void Guard_InventoryChangedAfterPlanning_IsStaleWithoutPreparingOrStartingTransactions(string change)
    {
        var wire = _fixture.AddExisting();
        _fixture.Cable["points"] = JArray.Parse("[[32,16],[48,16]]");
        _fixture.Cores[0]!["connection"] = CableDefinitionFixture.Reference(wire);
        string file = SaveInventory();
        string fingerprint = (string)_fixture.Plan()["fingerprint"]!;
        JObject inventory = JObject.Parse(File.ReadAllText(file));
        switch (change)
        {
            case "colour":
                inventory["wires"]![0]!["colour"] = "WH";
                break;
            case "label":
                inventory["wires"]![0]!["label"] = "8";
                break;
            case "crossSection":
                inventory["wires"]![0]!["crossSection"] = "2,5";
                break;
            case "cable":
                inventory["wires"]![0]!["cable"] = "-W8";
                break;
            case "digest":
                inventory["wires"]![0]!["locator"]!["digest"] = new string('d', 64);
                break;
            case "endpoint":
                inventory["wires"]![0]!["pins"]![0] = "17/5/0";
                break;
            case "geometry":
                inventory["wires"]![0]!["points"]![0]![0] = 44;
                break;
            case "newName":
                ((JArray)inventory["names"]!).Add("=F+A1-W3");
                break;
            case "incomplete":
                inventory["complete"] = false;
                break;
        }
        File.WriteAllText(file, inventory.ToString());
        var creator = new Creator(_fixture);
        var transactions = new FakeTransactions();
        var operation = new ConnectionCreateExecuteOperation(creator, creator, transactions);
        var error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new JObject { ["request"] = _fixture.Base.Request, ["fingerprint"] = fingerprint })).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(transactions.Calls);
        Assert.Equal(0, creator.Prepares);
    }

    [Fact]
    public void Execute_CableOnlyOrMixedRequest_RemainsCapabilityMissingBeforeAnyWrite()
    {
        var creator = new Creator(_fixture);
        var transactions = new FakeTransactions();
        var operation = new ConnectionCreateExecuteOperation(creator, creator, transactions);
        foreach (bool mixed in new[] { true, false })
        {
            if (!mixed)
            {
                var wire = _fixture.AddExisting();
                JObject cable = _fixture.Cable;
                cable["points"] = JArray.Parse("[[32,16],[48,16]]");
                _fixture.Cores[0]!["connection"] = CableDefinitionFixture.Reference(wire);
                _fixture.Base.Request["items"] = new JArray(cable);
            }
            SaveInventory();
            string fingerprint = (string)_fixture.Plan()["fingerprint"]!;
            var error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new JObject { ["request"] = _fixture.Base.Request, ["fingerprint"] = fingerprint })).Error;
            Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (error.Reason, error.Execution));
            Assert.Equal("connection.create.cable.execute", error.Metadata!["missing"]);
            Assert.Empty(transactions.Calls);
            Assert.Equal(0, creator.Prepares);
        }
    }

    [Fact]
    public void Definition_UnlistedExistingConnectionCrossed_FailsClosedWithNoPartialImpact()
    {
        _fixture.AddExisting();
        _fixture.Cable["points"] = JArray.Parse("[[16,16],[48,16]]");
        SaveInventory();
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeErrorException>(() => _fixture.Plan()).Error.Reason);
    }

    [Fact]
    public void Definition_UnlistedNodeAutoconnectCrossed_FailsClosed()
    {
        var wire = _fixture.AddExisting();
        _fixture.Base.AddPin(5, [60, 40], "Down", [58, 40, 62, 44]);
        JObject cable = _fixture.Cable;
        cable["points"] = JArray.Parse("[[32,16],[64,16]]");
        _fixture.Cores[0]!["connection"] = CableDefinitionFixture.Reference(wire);
        _fixture.Base.Request["items"] = new JArray(cable, ConnectionCreateFixture.Node("potential_point", [60, 8], "Up"));
        SaveInventory();
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeErrorException>(() => _fixture.Plan()).Error.Reason);
    }

    private string SaveInventory()
    {
        string file = Path.Combine(_root, "inventory.json");
        File.WriteAllText(file, BridgeJson.FromObject(_fixture.Inventory).ToString());
        _fixture.Base.Scene.ReadCables = () => JObject.Parse(File.ReadAllText(file)).ToObject<CableDefinitionPlanner.Inventory>(BridgeJson.Serializer)!;
        return file;
    }

    private sealed class Creator(CableDefinitionFixture fixture) : IConnectionCreator, IConnectionGenerator
    {
        internal int Prepares { get; private set; }
        public ConnectionCreatePlanner.Scene Read(string project, string page) => fixture.Base.Scene;
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => fixture.Base.Read(projectPath, args);
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => throw new InvalidOperationException("No interruption scope expected.");
        public ITransactionScopeFactory PlacementTransactions(string project) => throw new InvalidOperationException("No transaction expected.");
        public Action Prepare(string project, string page, JArray symbols) { Prepares++; throw new InvalidOperationException("Cable execution is unavailable."); }
        public List<string?[]> Endpoints(string project, string page) => throw new InvalidOperationException("No readback expected.");
        public void Undo(string project) => throw new InvalidOperationException("No undo expected.");
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new InvalidOperationException("No write expected.");
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new InvalidOperationException("No write expected.");
        public void Generate(string projectPath, IReadOnlyList<string> pages) => throw new InvalidOperationException("No generation expected.");
    }
}

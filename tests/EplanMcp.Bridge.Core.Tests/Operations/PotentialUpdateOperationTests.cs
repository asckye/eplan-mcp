using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PotentialUpdateOperationTests
{
    [Fact]
    public void Plan_ListsOnlyTheChangedValues_AndWritesNothing()
    {
        var world = new World();

        WritePlan plan = world.Plan(world.Input(("color", "BU"), ("cross_section", "1,5")));

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "potential", "WH", "BU"), (change.Action, change.Kind, change.OldValue, change.NewValue));
        Assert.Equal("N (4/1) color", change.Name);
        Assert.Equal("WH", world.Values["color"]);
        Assert.Empty(world.Transactions.Calls);
    }

    [Fact]
    public void Execute_WritesReadsBackAndHintsAtRegeneration()
    {
        var world = new World();
        JObject input = world.Input(("color", "BU"), ("potential_type", 3));

        JObject result = world.Execute(input, world.Plan(input));

        Assert.Equal(("BU", "3"), (world.Values["color"], world.Values["potential_type"]));
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(("WH", "N"), ((string?)result["items"]![0]!["before"]!["color"], (string?)result["items"]![0]!["before"]!["potential_name"]));
        Assert.Contains("rebuild:true", (string?)result["hint"], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReadBackMismatch_RollsBack()
    {
        var world = new World { Ignore = true };
        JObject input = world.Input(("color", "BU"));
        WritePlan plan = world.Plan(input);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => world.Execute(input, plan));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Equal("WH", world.Values["color"]);
    }

    [Fact]
    public void Execute_ValueChangedAfterThePlan_IsStale()
    {
        var world = new World();
        JObject input = world.Input(("color", "BU"));
        WritePlan plan = world.Plan(input);
        world.Values["color"] = "RD";

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => world.Execute(input, plan));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }

    [Fact]
    public void Plan_AnotherKindOrNoProperty_IsInvalid()
    {
        var line = new World("Line");
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => line.Plan(line.Input(("color", "BU")))).Error.Reason);

        var world = new World();
        BridgeErrorException none = Assert.Throws<BridgeErrorException>(() => world.Plan(world.Input()));
        Assert.Contains("at least one property", none.Error.Metadata!["violations"], StringComparison.Ordinal);
        BridgeErrorException type = Assert.Throws<BridgeErrorException>(() => world.Plan(world.Input(("potential_type", "3"))));
        Assert.Contains("integer", type.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    private sealed class World : IPageDrawingReader, IPotentialEditor
    {
        public World(string kind = "PotentialDefinition")
        {
            Page = new() { Project = "project", Page = "page", GridSize = 4, Placements = [new() { Kind = kind, Locator = new() { Project = "project", Page = "page", Type = 4, Oid = 1, Digest = new string('a', 64) } }] };
            Transactions.Snapshot = () =>
            {
                var old = new Dictionary<string, string?>(Values);
                return () => { Values.Clear(); foreach (var pair in old) { Values[pair.Key] = pair.Value; } };
            };
        }

        public PageDrawingGeometry Page { get; }

        public Dictionary<string, string?> Values { get; } = new() { ["potential_name"] = "N", ["signal_name"] = null, ["color"] = "WH", ["cross_section"] = "1,5", ["potential_value"] = null, ["potential_type"] = "2" };

        public FakeTransactions Transactions { get; } = new();

        public bool Ignore { get; init; }

        public PageDrawingGeometry? Read(string projectPath, string pageName) => JObject.FromObject(Page).ToObject<PageDrawingGeometry>()!;

        public IReadOnlyList<IReadOnlyDictionary<string, string?>> Read(string project, string page, IReadOnlyList<DrawingLocator> items, IReadOnlyList<string> names) =>
            [.. items.Select(_ => (IReadOnlyDictionary<string, string?>)names.ToDictionary(n => n, n => Values[n]))];

        public Action Prepare(string project, string page, IReadOnlyList<(DrawingLocator Locator, IReadOnlyDictionary<string, string> Values)> changes) => () =>
        {
            if (Ignore)
            { return; }
            foreach (var change in changes.SelectMany(c => c.Values))
            { Values[change.Key] = change.Value; }
        };

        public JObject Input(params (string Name, object Value)[] values)
        {
            var item = new JObject { ["locator"] = JObject.FromObject(Page.Placements[0].Locator, Bridge.Core.Protocol.BridgeJson.Serializer) };
            foreach (var (name, value) in values)
            { item[name] = JToken.FromObject(value); }
            return new JObject { ["project"] = "project", ["page"] = "page", ["items"] = new JArray(item) };
        }

        private PotentialUpdateOperation Operation(bool execute) => new(new FakeCatalog(new OpenProject { Name = "project", Path = "project" }), this, this, Transactions, execute);

        public WritePlan Plan(JObject input) => ((JObject)Operation(false).Execute(input)!).ToObject<WritePlan>()!;

        public JObject Execute(JObject input, WritePlan plan) => (JObject)Operation(true).Execute(new JObject { ["request"] = input.DeepClone(), ["fingerprint"] = plan.Fingerprint })!;
    }
}

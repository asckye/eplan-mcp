using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionExistingNetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_LiveBusAndSeparateSupport_PredictsTouchedNetsWithoutInventingPotential(bool integerCoordinates)
    {
        var fake = new Rehearsal { IntegerCoordinates = integerCoordinates };
        JObject plan = fake.Plan();
        JToken support = Assert.Single(plan["logicalAfterGenerate"]!, g => (bool)g["potentialStartIsNull"]!);
        Assert.Equal(["17/69868/0"], support["pins"]!.Values<string>());
        JToken bus = Assert.Single(plan["logicalAfterGenerate"]!, g => g["pins"]!.Values<string>().Contains("17/69837/0", StringComparer.Ordinal));
        Assert.False((bool)bus["potentialStartIsNull"]!);
        string[] busPins = ["17/69837/0", "17/69838/0", "17/69839/0", "17/69840/0", "17/69841/0", "17/69852/0"];
        Assert.All(busPins,
            pin => Assert.Contains(pin, bus["pins"]!.Values<string>()));
        Assert.DoesNotContain(plan["logicalAfterGenerate"]!, g => g["pins"]!.Values<string>().Contains("17/80000/0", StringComparer.Ordinal));
        Assert.Equal(6, plan["logicalAfterGenerate"]!.Count());
    }

    [Fact]
    public void Plan_ExistingCornerFreePortWithoutInterruption_TraversesExistingNodeCentres()
    {
        var fake = new Rehearsal();
        fake.Fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [116, 112], "Up"));
        fake.Fixture.Symbols.Add(ConnectionCreateFixture.Variant("PCP", "PotentialTerminal", 3, "Up"));
        fake.Fixture.Scene.SymbolBounds["PCP/3"] = [-1, -1, 1, 1];
        JToken group = Assert.Single(fake.Fixture.Plan()["logicalAfterGenerate"]!);
        Assert.Equal(6, group["pins"]!.Count());
        Assert.Contains("17/69852/0", group["pins"]!.Values<string>());
        Assert.True((bool)group["potentialStartIsNull"]!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_LiveStarPlusInterruptionEnds_CommitsWithSeparateSupport(bool reverseInterruption)
    {
        var fake = new Rehearsal();
        if (reverseInterruption)
        { fake.Generated[^1] = [fake.Generated[^1][1], fake.Generated[^1][0]]; }
        Assert.Equal(ExecutionState.Committed, (string?)fake.Execute()["execution"]);
        Assert.Equal(2, fake.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal(0, fake.Undos);
    }

    [Theory]
    [InlineData("missing_interruption")]
    [InlineData("missing_bus_interruption")]
    public void Execute_InterruptionWithoutDeviceBeyondIt_CommitsWithoutItsConnection(string missing)
    {
        // Live customer rehearsal: EPLAN connects devices through interruption points, so a point whose counterpart side
        // has no device yet gets no generated connection; that is not a mismatch.
        var fake = new Rehearsal();
        if (missing == "missing_interruption")
        { fake.Generated.RemoveAt(fake.Generated.Count - 1); }
        else
        { fake.Generated.RemoveAll(p => p.Contains(InterruptionPointPlanning.Endpoint(ConnectionCreateFixture.PageName, "I2"), StringComparer.Ordinal)); }
        Assert.Equal(ExecutionState.Committed, (string?)fake.Execute()["execution"]);
        Assert.Equal(0, fake.Undos);
    }

    [Theory]
    [InlineData("missing_support")]
    [InlineData("unrelated_net")]
    [InlineData("cross_net")]
    [InlineData("lost_baseline")]
    [InlineData("lost_untouched_baseline")]
    public void Execute_MissingOrUnplannedConnectivity_RollsBack(string failure)
    {
        var fake = new Rehearsal();
        switch (failure)
        {
            case "missing_support":
                fake.Generated.RemoveAll(p => p[0] is null);
                break;
            case "unrelated_net":
                fake.Generated.Add(["17/80000/0", "17/69868/0"]);
                break;
            case "cross_net":
                fake.Generated.Add(["17/69841/0", "17/69868/0"]);
                break;
            case "lost_baseline":
                fake.Generated.RemoveAt(0);
                break;
            case "lost_untouched_baseline":
                fake.Generated.RemoveAll(p => p[0] == "17/80000/0");
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(0, fake.State);
        Assert.Equal(1, fake.Undos);
    }

    private sealed class Rehearsal : IConnectionCreator, IConnectionGenerator
    {
        internal ConnectionCreateFixture Fixture { get; } = new();
        internal FakeTransactions Transactions { get; } = new();
        internal List<string?[]> Generated { get; } = [];
        internal int State { get; private set; }
        internal int Undos { get; private set; }
        internal bool IntegerCoordinates { get; init; }
        private readonly JArray _points = [];
        private readonly List<double[][]> _added = [];
        internal Rehearsal()
        {
            ConnectionCreatePlanner.Scene scene = Fixture.Scene;
            scene.Pins.Clear();
            scene.Page.Placements.Clear();
            scene.Page.PageSize = [500, 300];
            Fixture.Symbols.Clear();
            Fixture.Symbols.Add(ConnectionCreateFixture.Variant("BPF", "InterruptionPoint", 0, "Up"));
            Fixture.Symbols.Add(ConnectionCreateFixture.Variant("BPF", "InterruptionPoint", 1, "Down"));
            Fixture.Symbols.Add(ConnectionCreateFixture.Variant("PCP", "PotentialTerminal", 2, "Down"));
            scene.SymbolBounds["BPF/0"] = scene.SymbolBounds["BPF/1"] = [-1, -1, 1, 1];
            Fixture.Request["items"] = new JArray();
            Pin(69852, 0, [100, 130], "Up");
            Pin(69852, 1, [100, 127.25], "Down");
            Node(900, [100, 136], "Up", "Down", "Right");
            Node(901, [116, 136], "Left", "Down");
            Node(902, [100, 220], "Down", "Right");
            Line([100, 130], [100, 136]);
            Line([100, 136], [116, 136]);
            Line([100, 136], [100, 220]);
            for (int i = 0; i < 5; i++)
            {
                double x = 140 + 40 * i;
                Pin(69837 + i, 0, [x, 200], "Up");
                Node(910 + i, [x, 220], i == 4 ? ["Left", "Down"] : ["Left", "Right", "Down"]);
                Line([x - 40, 220], [x, 220]);
                Line([x, 220], [x, 200]);
                if (i < 4)
                { scene.Logical.Add(new() { StartDevice = FormattableString.Invariant($"17/{69837 + i}/0"), EndDevice = "17/69841/0" }); }
            }
            scene.Logical.Add(new() { StartDevice = "17/69852/0", EndDevice = "17/69841/0" });
            Branch(69839, 69855, 930, 220);
            Branch(69840, 69856, 940, 260);
            Pin(69860, 0, [340, 200], "Up");
            Pin(69868, 0, [40, 44], "Up");
            Pin(69868, 1, [40, 32], "Down");
            Pin(80000, 0, [404, 84], "Right");
            Pin(80001, 0, [436, 84], "Left");
            Line([404, 84], [436, 84]);
            scene.Logical.Add(new() { StartDevice = "17/80000/0", EndDevice = "17/80001/0" });
            Generated.AddRange(scene.Logical.Select(c => new string?[] { c.StartDevice, c.EndDevice }));
            Generated.Add([null, "17/69868/0"]);
            ((JArray)Fixture.Request["items"]!).Add(ConnectionCreateFixture.Node("potential_point", [40, 56], "Down"));
            _added.Add([[40, 56], [40, 44]]);
            Interruption("I1", [100, 112], [100, 127.25], "Up", "17/69852/1");
            Interruption("I2", [116, 112], [116, 136], "Up", "17/69841/0");
            Interruption("I3", [236, 112], [236, 136], "Up", "17/69839/1");
            Interruption("I4", [276, 112], [276, 136], "Up", "17/69840/1");
            Interruption("I5", [340, 252], [340, 200], "Down", "17/69860/0");
            Transactions.Snapshot = () => { int state = State; return () => State = state; };
        }
        private void Pin(long oid, int index, double[] at, string direction) => Fixture.Scene.Pins.Add(
            FormattableString.Invariant($"17/{oid}/{index}"), new() { Location = at, Direction = direction });
        private void Node(long oid, double[] at, params string[] directions)
        { for (int i = 0; i < directions.Length; i++) { Pin(oid, i, at, directions[i]); } }
        private void Line(double[] from, double[] to) => Fixture.Scene.Graphical.Add([from, to]);
        private void Branch(long contact, long terminal, long node, double x)
        {
            Pin(contact, 1, [x, 188], "Down");
            Pin(terminal, 0, [x, 130], "Up");
            Node(node, [x, 136], "Up", "Down", "Right");
            Node(node + 1, [x + 16, 136], "Left", "Down");
            Line([x, 188], [x, 136]);
            Line([x, 136], [x, 130]);
            Line([x, 136], [x + 16, 136]);
            Fixture.Scene.Logical.Add(new() { StartDevice = FormattableString.Invariant($"17/{contact}/1"), EndDevice = FormattableString.Invariant($"17/{terminal}/0") });
        }
        private void Interruption(string name, double[] at, double[] to, string direction, string device)
        {
            ((JArray)Fixture.Request["items"]!).Add(new JObject
            {
                ["kind"] = "interruption_point",
                ["name"] = name,
                ["direction"] = direction,
                ["position"] = new JArray(at),
                ["counterpart_pages"] = new JArray(InterruptionPointFixture.PeerPage),
            });
            JObject point = InterruptionPointFixture.Point(ConnectionCreateFixture.PageName, 1000 + _points.Count, at, direction);
            point["name"] = name;
            _points.Add(point);
            _added.Add([at, to]);
            Generated.Add([device, InterruptionPointPlanning.Endpoint(ConnectionCreateFixture.PageName, name)]);
        }
        internal JObject Plan() => ConnectionCreatePlanner.Plan(Fixture.Request.ToString(), Fixture.Scene, this);
        internal JObject Execute() => Assert.IsType<JObject>(new OperationContext("live-rehearsal", true, null).Run(() =>
            new ConnectionCreateExecuteOperation(this, this, Transactions).Execute(new JObject { ["request"] = Fixture.Request.DeepClone(), ["fingerprint"] = Plan()["fingerprint"] })));
        public ConnectionCreatePlanner.Scene Read(string project, string page) => State == 0 ? Fixture.Scene : new() { Graphical = [.. Fixture.Scene.Graphical, .. _added] };
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => Fixture.Read(projectPath, args);
        public Action Prepare(string project, string page, JArray symbols) => () => State = 1;
        public List<string?[]> Endpoints(string project, string page) => page == ConnectionCreateFixture.PageName ? Generated : [];
        public void Generate(string projectPath, IReadOnlyList<string> pages) => State = 2;
        public void Undo(string project) { State--; Undos++; }
        public ITransactionScopeFactory PlacementTransactions(string project) => Transactions;
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => new()
        {
            ["points"] = State == 0 ? new JArray() : _points.DeepClone(),
            ["pages"] = new JArray(pages.Select(p => new JObject
            {
                ["page"] = p,
                ["drawing"] = BridgeJson.FromObject(Fixture.Scene.Page),
                ["pins"] = p == ConnectionCreateFixture.PageName ? BridgeJson.FromObject(Fixture.Scene.Pins) : new JObject(),
                ["graphical"] = p == ConnectionCreateFixture.PageName ? GraphicalEvidence() : new JArray(),
                ["logical"] = p == ConnectionCreateFixture.PageName ? BridgeJson.FromObject(Fixture.Scene.Logical) : new JArray(),
            })),
        };
        private JArray GraphicalEvidence()
        {
            JArray lines = JArray.FromObject(Fixture.Scene.Graphical);
            if (IntegerCoordinates)
            {
                foreach (JValue value in lines.SelectMany(s => s).SelectMany(p => p).Cast<JValue>())
                { value.Value = Convert.ToInt64(value.Value, System.Globalization.CultureInfo.InvariantCulture); }
            }
            return lines;
        }
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
    }
}

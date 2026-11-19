using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class UnplacedFunctionDeleteTests
{
    [Fact]
    public void PlanListsEachExactTargetAndCountsOnlyColonDependents()
    {
        var world = new World();
        WritePlan plan = world.Plan();
        JObject preview = JObject.Parse(plan.Details!["preview"]);
        Assert.Equal(["-X1", "-K1"], preview["unplaced"]!.Values<string>());
        Assert.Equal([1L, 4L, 3L], preview["items"]!.Select(item => (long)item["oid"]!));
        Assert.Equal(1, (int)preview["dependents"]!);
        Assert.Equal(plan.Fingerprint, (string?)preview["fingerprint"]);
        Assert.All(plan.Changes, change => Assert.Equal(("delete", "function"), (change.Action, change.Kind)));
        Assert.Equal("-X1 (TerminalDefText, unplaced)", plan.Changes[0].Name);
        Assert.Equal("1 functions named -X1:… exist", plan.Changes[0].OldValue);
        Assert.Null(plan.Changes[2].OldValue);
        Assert.Empty(world.Transactions.Calls);
        Assert.Equal(0, world.Backups);
        world.Rows.Reverse();
        Assert.Equal(plan.Fingerprint, world.Plan().Fingerprint);
    }

    [Fact]
    public void ExecutionDeletesExactlyTargetsInOneTransaction()
    {
        var world = new World();
        JObject result = world.Execute(world.Plan());
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([2L, 5L], world.Rows.Select(row => row.Oid));
        Assert.Equal(1, world.Backups);
        Assert.Equal(["Begin AI: eplan_placement_delete", "Commit", "CloseUndo", "Dispose"], world.Transactions.Calls);
    }

    [Fact]
    public void MissingTagIsNamed()
    {
        var world = new World();
        world.Input["unplaced"] = new JArray("-missing");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan()).Error;
        Assert.Equal(ErrorReason.DeviceNotFound, error.Reason);
        Assert.Contains("-missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlacedTargetIsRefusedWithPathAndPage()
    {
        var world = new World();
        world.Rows[0].Placed = true;
        world.Rows[0].Page = "=F+A/42";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/unplaced/0", error.Message, StringComparison.Ordinal);
        Assert.Contains("=F+A/42", error.Message, StringComparison.Ordinal);
        Assert.Contains("items from eplan_page_get include_drawings", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAllInputViolationsBeforeReading()
    {
        var world = new World();
        world.Input["project"] = " ";
        world.Input["unplaced"] = new JArray("-X1", 123, "x", "abc\n", "-X1", new string('x', 129));
        world.Input["page"] = "page";
        world.Input["items"] = new JArray();
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        foreach (string path in new[] { "/project", "/page", "/items", "/unplaced/1", "/unplaced/2", "/unplaced/3", "/unplaced/4", "/unplaced/5" })
        {
            Assert.Contains(path, error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(0, world.Reads);
    }

    [Fact]
    public void ParentAndChildAreBothReportedBeforeReading()
    {
        var world = new World();
        world.Input["unplaced"] = new JArray("-X1:1", "-K1", "-X1");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        foreach (string path in new[] { "/unplaced/0", "/unplaced/2" })
        {
            Assert.Contains(path + " '-X1:1' depends on '-X1'; delete them in separate calls.", error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(0, world.Reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void InvalidBatchSizeIsRefusedBeforeReading(int count)
    {
        var world = new World();
        world.Input["unplaced"] = new JArray(Enumerable.Range(0, count).Select(i => $"-X{i}"));
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, world.Reads);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("dependent")]
    [InlineData("placed")]
    [InlineData("gone")]
    public void StalePlanRefusesBeforeBackupOrTransaction(string change)
    {
        var world = new World();
        WritePlan plan = world.Plan();
        switch (change)
        {
            case "path":
                plan.ProjectPath = "other";
                break;
            case "dependent":
                world.Rows[1].Main = true;
                break;
            case "placed":
                world.Rows[0].Placed = true;
                break;
            case "gone":
                world.Rows.RemoveAll(row => row.Name == "-X1");
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Execute(plan)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(world.Transactions.Calls);
        Assert.Equal(0, world.Backups);
    }

    [Theory]
    [InlineData("changed", "-X1:1")]
    [InlineData("missing", "-X1:1")]
    [InlineData("added", "-X1:9")]
    [InlineData("remaining", "-X1")]
    [InlineData("replacement", "-K1")]
    public void FailedReadbackReportsDifferenceAndVerifiesRollback(string fault, string name)
    {
        var world = new World { Fault = fault };
        WritePlan plan = world.Plan();
        BridgeError error = Assert.Throws<BridgeErrorException>(() => world.Execute(plan)).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("Deletion left targets behind or changed other functions:", error.Message, StringComparison.Ordinal);
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
        if (fault == "changed")
        {
            Assert.Contains("another page", error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(["Begin AI: eplan_placement_delete", "Commit", "Rollback", "Dispose"], world.Transactions.Calls);
        Assert.Equal(plan.Fingerprint, world.Plan().Fingerprint);
    }

    private sealed class World : IUnplacedFunctionRemover
    {
        public List<NamedFunctionRow> Rows { get; private set; } =
        [
            new() { Name = "-X1", Type = 4, Oid = 1, Category = "TerminalDefText", Main = true },
            new() { Name = "-X1:1", Type = 4, Oid = 2, Category = "Terminal", Placed = true, Page = "page" },
            new() { Name = "-K1", Type = 4, Oid = 3, Category = "Coil" },
            new() { Name = "-X1", Type = 4, Oid = 4, Category = "TerminalDefText" },
            new() { Name = "-X10", Type = 4, Oid = 5, Category = "TerminalDefText" }
        ];
        public FakeTransactions Transactions { get; } = new();
        public JObject Input { get; } = new() { ["project"] = "Name", ["unplaced"] = new JArray("-X1", "-K1") };
        public string? Fault { get; init; }
        public int Reads { get; private set; }
        public int Backups { get; private set; }

        public World()
        {
            Transactions.Snapshot = () =>
            {
                var saved = JArray.FromObject(Rows).ToObject<List<NamedFunctionRow>>()!;
                return () => Rows = saved;
            };
        }

        public IReadOnlyList<NamedFunctionRow> Find(string project, IReadOnlyList<string> tags)
        {
            Reads++;
            return JArray.FromObject(Rows.Where(row => tags.Any(tag => row.Name == tag || row.Name.StartsWith(tag + ":", StringComparison.Ordinal))))
                .ToObject<NamedFunctionRow[]>()!;
        }

        public Action Prepare(string project, IReadOnlyList<NamedFunctionRow> rows) => () =>
        {
            Rows.RemoveAll(row => rows.Any(target => row.Type == target.Type && row.Oid == target.Oid) && !(Fault == "remaining" && row.Oid == 1));
            switch (Fault)
            {
                case "changed":
                    Rows[0].Page = "another page";
                    break;
                case "missing":
                    Rows.RemoveAt(0);
                    break;
                case "added":
                    Rows.Add(new() { Name = "-X1:9", Oid = 9, Type = 4 });
                    break;
                case "replacement":
                    Rows.Add(new() { Name = "-K1", Oid = 9, Type = 4 });
                    break;
            }
        };

        private UnplacedFunctionDeleteOperation Operation(bool execute) =>
            new(new FakeCatalog(new OpenProject { Name = "Name", Path = "path" }), this, Transactions, execute, (_, _) => Backups++);

        public WritePlan Plan() => ((JObject)Operation(false).Execute(Input)!).ToObject<WritePlan>()!;

        public JObject Execute(WritePlan plan) => (JObject)Operation(true).Execute(new JObject
        {
            ["request"] = Input,
            ["fingerprint"] = plan.Fingerprint,
            ["projectPath"] = plan.ProjectPath
        })!;
    }
}

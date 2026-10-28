using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementMoveExecutionTests
{
    [Fact]
    public void Closure_SameOidChildrenFollowAndOutsideStaysUnchanged_OneUndo()
    {
        var mover = new Mover();
        JObject result = mover.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([25d, 20d], mover.Scene.Page.Placements[0].Location);
        Assert.Equal([27d, 22d], mover.Scene.Page.Placements[1].Location);
        Assert.Equal([28d, 23d], mover.Scene.Page.Placements[2].Location);
        Assert.Equal([40d, 20d], mover.Scene.Page.Placements[3].Location);
        Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], mover.Transactions.Calls);
    }

    [Theory]
    [InlineData("location")]
    [InlineData("child")]
    [InlineData("outside")]
    [InlineData("pins")]
    [InlineData("graphical")]
    [InlineData("logical")]
    [InlineData("cancel")]
    public void ReadbackMismatch_RestoresWholeSceneWithoutClosingUndo(string failure)
    {
        var mover = new Mover { Failure = failure };
        string before = BridgeJson.FromObject(mover.Scene).ToString();
        BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute()).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(before, BridgeJson.FromObject(mover.Scene).ToString());
        Assert.DoesNotContain("CloseUndo", mover.Transactions.Calls);
        Assert.Contains("Rollback", mover.Transactions.Calls);
    }

    [Theory]
    [InlineData("Rollback")]
    [InlineData("Commit")]
    [InlineData("restore")]
    public void CannotProveRestoration_ReportsUnknown(string failure)
    {
        var mover = new Mover { Failure = failure == "Commit" ? null : "graphical", BreakRestore = failure == "restore" };
        mover.Transactions.FailAt = failure == "restore" ? null : failure;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute()).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    internal sealed class Mover : IPlacementMover
    {
        internal MoveFixture Fixture { get; } = new();
        internal ConnectionCreatePlanner.Scene Scene { get; set; }
        internal FakeTransactions Transactions { get; } = new();
        internal string? Failure { get; set; }
        internal bool BreakRestore { get; set; }
        internal int Moves { get; private set; }
        internal Mover()
        {
            Fixture.Child('b', [22, 22]);
            Fixture.Child('c', [23, 23]);
            Fixture.Connect();
            Fixture.Request["moves"]![0]!["delta_mm"] = new JArray(5, 0);
            Scene = Fixture.Scene;
            Transactions.Snapshot = () =>
            {
                ConnectionCreatePlanner.Scene saved = Clone(Scene);
                return () => { Scene = saved; if (BreakRestore) { Scene.Page.GridSize = 2; } };
            };
        }
        public ConnectionCreatePlanner.Scene Read(string project, string page)
        {
            ConnectionCreatePlanner.Scene copy = Clone(Scene);
            if (Moves > 0 && Transactions.Calls.Contains("Commit") && !Transactions.Calls.Contains("Rollback"))
            {
                copy.Graphical = Failure == "graphical" ? [] : [[[25, 20], [40, 20]]];
                if (Failure == "logical")
                { copy.Logical.Clear(); }
            }
            return copy;
        }
        public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
        {
            foreach (JToken row in moves)
            {
                DrawingPlacement placement = Scene.Page.Placements.Single(p => p.Locator.Digest == (string)row["locator"]!["digest"]!);
                placement.Location = row["after"]!.Values<double>().ToArray();
                placement.Bounds = row["bounds"]!.Values<double>().ToArray();
                placement.Locator.Digest = new string('e', 64);
                Moves++;
                if (Failure == "batch")
                { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Injected mid-batch failure."); }
            }
            Scene.Pins["17/1/0"].Location = [25, 20];
            if (Failure == "location")
            { Scene.Page.Placements[0].Location = [20, 20]; }
            if (Failure == "child")
            { Scene.Page.Placements[1].Location = [22, 22]; }
            if (Failure == "outside")
            { Scene.Page.Placements[3].Location = [41, 20]; }
            if (Failure == "pins")
            { Scene.Pins["17/1/0"].Location = [20, 20]; }
            if (Failure == "cancel")
            { OperationContext.Current.Cancel(); }
        };
        internal JObject Execute(string? fingerprint = null) => Assert.IsType<JObject>(new OperationContext("move", true, null).Run(() => PlacementMoveExecution.Execute(this, Transactions, Fixture.Request,
            fingerprint ?? (string)PlacementMovePlanner.Plan(Fixture.Request.ToString(), Scene)["fingerprint"]!)));
        private static ConnectionCreatePlanner.Scene Clone(ConnectionCreatePlanner.Scene scene) => BridgeJson.FromObject(scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
    }
}

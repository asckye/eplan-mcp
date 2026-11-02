using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementMoveExecutionTests
{
    [Theory]
    [InlineData("eplan_placement_update", false)]
    [InlineData("eplan_placement_update", true)]
    [InlineData("eplan_placement_edit", false)]
    [InlineData("eplan_placement_edit", true)]
    public void MeasuredPropertyOutlineOutsideBoundary_RefusesBeforeSizeOnlyNormalization(string tool, bool afterCommit)
    {
        var mover = new Mover { PropertyBoundsOffset = afterCommit ? 0 : 75, Failure = afterCommit ? "layout_after_commit" : null };
        string before = BridgeJson.FromObject(mover.Scene).ToString();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute(tool: tool)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("R1", error.Message, StringComparison.Ordinal);
        Assert.Contains("101", error.Metadata!["layoutViolations"], StringComparison.Ordinal);
        Assert.Equal(before, BridgeJson.FromObject(mover.Scene).ToString());
        Assert.Equal("Begin AI: " + tool, mover.Transactions.Calls[0]);
        Assert.Equal(afterCommit ? 1 : 0, mover.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Contains("Rollback", mover.Transactions.Calls);
        Assert.DoesNotContain("CloseUndo", mover.Transactions.Calls);
    }

    [Fact]
    public void UnknownMovedOutline_IsMeasuredCleanlyBeforeCommitAndCommitsOnce()
    {
        var mover = new Mover();
        mover.Scene.Page.Placements[0].Bounds = null;
        JObject result = mover.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Contains("after write", (string?)result["warning"], StringComparison.Ordinal);
        Assert.Equal(1, mover.Transactions.Calls.Count(c => c == "Commit"));
    }
    [Fact]
    public void Closure_SameOidChildrenFollowAndOutsideStaysUnchanged_OneUndo()
    {
        var mover = new Mover();
        JObject result = mover.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([25d, 20d], mover.Scene.Page.Placements[0].Location);
        Assert.Equal([27d, 22d], mover.Scene.Page.Placements[1].Location);
        Assert.Equal([29d, 24d], mover.Scene.Page.Placements[2].Location);
        Assert.Equal([40d, 20d], mover.Scene.Page.Placements[3].Location);
        Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], mover.Transactions.Calls);
    }

    [Fact]
    public void MovedParent_FollowingPropertyBoundsRecomputed_ReturnsMeasuredOutlines()
    {
        var mover = new Mover { PropertyBoundsOffset = 4.27875 };

        JObject result = mover.Execute();

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([25d, 20d], mover.Scene.Page.Placements[0].Location);
        Assert.Equal([27d, 22d], mover.Scene.Page.Placements[1].Location);
        Assert.Equal([30.27875, 21d, 32.27875, 23d], result["moves"]![1]!["bounds"]!.Values<double>());
        Assert.Equal(mover.Scene.Page.Placements[1].Bounds, result["moves"]![1]!["bounds"]!.Values<double>());
        Assert.Equal([40d, 20d], mover.Scene.Page.Placements[3].Location);
        Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], mover.Transactions.Calls);
    }

    [Fact]
    public void TextMismatch_ReportsMeasuredBoundsBeforeSizeOnlyNormalization()
    {
        var mover = new Mover { Failure = "child", PropertyBoundsOffset = 4.27875 };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute()).Error;

        JObject mismatch = JObject.Parse(error.Metadata!["moveMismatch"]);
        Assert.Equal("text", (string?)mismatch["check"]);
        JToken change = Assert.Single(mismatch["changed"]!);
        Assert.Equal([22d, 22d], change["before"]!["location"]!.Values<double>());
        Assert.Equal([27d, 22d], change["expected"]!["location"]!.Values<double>());
        Assert.Equal([22d, 22d], change["after"]!["location"]!.Values<double>());
        Assert.Equal([30.27875, 21d, 32.27875, 23d], change["after"]!["bounds"]!.Values<double>());
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
        if (failure is not ("cancel" or "pins"))
        {
            JObject mismatch = JObject.Parse(error.Metadata!["moveMismatch"]);
            string check = failure switch { "child" => "text", "pins" => "pins", "logical" or "graphical" => "connections", _ => "closure" };
            Assert.Equal(check, (string?)mismatch["check"]);
            Assert.Contains(check + ":", error.Message, StringComparison.Ordinal);
            Assert.NotEmpty(mismatch["differences"]!);
        }
    }

    [Fact]
    public void PinsMismatch_ReportsAddedPinAndRestoresTheScene()
    {
        var mover = new Mover { Failure = "extra_pin" };
        string before = BridgeJson.FromObject(mover.Scene).ToString();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute()).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        JObject mismatch = JObject.Parse(error.Metadata!["moveMismatch"]);
        Assert.Equal("pins", (string?)mismatch["check"]);
        JToken difference = Assert.Single(mismatch["differences"]!);
        Assert.Equal("/17~11~11", (string?)difference["path"]);
        Assert.Equal(JTokenType.Null, difference["expected"]!.Type);
        Assert.Equal([25d, 20d], difference["actual"]!["location"]!.Values<double>());
        Assert.Equal(before, BridgeJson.FromObject(mover.Scene).ToString());
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
        internal double PropertyBoundsOffset { get; init; }
        internal int Moves { get; private set; }
        internal Mover()
        {
            Fixture.Child('b', [22, 22]);
            Fixture.Child('c', [24, 24]);
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
                if (Failure == "layout_after_commit")
                { copy.Page.Placements[1].Bounds = [101, 21, 103, 23]; }
            }
            return copy;
        }
        public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
        {
            foreach (JToken row in moves)
            {
                DrawingPlacement placement = Scene.Page.Placements.Single(p => p.Locator.Digest == (string)row["locator"]!["digest"]!);
                placement.Location = row["after"]!.Values<double>().ToArray();
                placement.Bounds = row["bounds"]!.Type == JTokenType.Null
                    ? [placement.Location[0] - 1, placement.Location[1] - 1, placement.Location[0] + 1, placement.Location[1] + 1]
                    : row["bounds"]!.Values<double>().ToArray();
                if (placement.Kind == "PropertyPlacement")
                { placement.Bounds[0] += PropertyBoundsOffset; placement.Bounds[2] += PropertyBoundsOffset; }
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
            if (Failure == "extra_pin")
            { Scene.Pins["17/1/1"] = new() { Location = [25, 20], Direction = "Right" }; }
            if (Failure == "cancel")
            { OperationContext.Current.Cancel(); }
        };
        internal JObject Execute(string? fingerprint = null, string tool = "eplan_placement_update") => Assert.IsType<JObject>(new OperationContext("move", true, null).Run(() => PlacementMoveExecution.Execute(this, Transactions, Fixture.Request,
            fingerprint ?? (string)PlacementMovePlanner.Plan(Fixture.Request.ToString(), Scene)["fingerprint"]!, tool)));
        private static ConnectionCreatePlanner.Scene Clone(ConnectionCreatePlanner.Scene scene) => BridgeJson.FromObject(scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
    }
}

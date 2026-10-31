using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyPlacementMoveTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void IndividualText_SameParentAndPropertyDifferentSlots_OneUndoAndUnchangedSiblings(int angle)
    {
        var fake = new Mover(angle);
        string outside = BridgeJson.FromObject(fake.Scene.Page.Placements.Where(p => p != fake.Child)).ToString();
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([50d, 50d], fake.Child.Location);
        Assert.Equal(angle.ToString(System.Globalization.CultureInfo.InvariantCulture), fake.Child.Properties["angle_deg"]);
        Assert.Equal(outside, BridgeJson.FromObject(fake.Scene.Page.Placements.Where(p => p != fake.Child)).ToString());
        Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], fake.Transactions.Calls);
        Assert.True((bool)fake.Plan()["noOp"]!);
        fake.Transactions.Calls.Clear();
        Assert.Equal(ExecutionState.NotStarted, (string?)fake.Execute()["execution"]);
        Assert.Empty(fake.Transactions.Calls);
    }

    [Theory]
    [InlineData("angle")]
    [InlineData("position")]
    [InlineData("sibling")]
    [InlineData("parent")]
    [InlineData("logical")]
    [InlineData("committed")]
    [InlineData("cancel")]
    public void IncorrectReadbackOrCancel_RestoresEverythingWithoutEmptyUndo(string failure)
    {
        var fake = new Mover(90) { Failure = failure };
        string before = BridgeJson.FromObject(fake.Scene).ToString();
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(before, BridgeJson.FromObject(fake.Scene).ToString());
        Assert.DoesNotContain("CloseUndo", fake.Transactions.Calls);
    }

    [Theory]
    [InlineData("angle")]
    [InlineData("fraction")]
    [InlineData("selector")]
    [InlineData("index")]
    [InlineData("parent")]
    [InlineData("duplicate")]
    [InlineData("mixed")]
    [InlineData("bounds")]
    [InlineData("layer")]
    [InlineData("nan")]
    public void InvalidOrStaleInputs_NeverStartATransaction(string invalid)
    {
        var fake = new Mover(90);
        JToken move = fake.Request["property_moves"]![0]!;
        switch (invalid)
        {
            case "angle":
                move["angle_deg"] = 45;
                break;
            case "fraction":
                move["angle_deg"] = 90.0;
                break;
            case "selector":
                move["property_id"] = 20011;
                break;
            case "index":
                move["property_index"] = JToken.Parse("2147483648");
                break;
            case "parent":
                move["parent"]!["digest"] = new string('f', 64);
                break;
            case "duplicate":
                ((JArray)fake.Request["property_moves"]!).Add(move.DeepClone());
                break;
            case "mixed":
                fake.Request["moves"] = new JArray();
                break;
            case "bounds":
                move["x_mm"] = 0;
                break;
            case "layer":
                fake.Child.Properties["angle_deg"] = "from_layer";
                break;
            case "nan":
                move["x_mm"] = double.NaN;
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Plan()).Error;
        Assert.Equal(invalid is "parent" or "selector" ? ErrorReason.PlanStale : ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Execution);
        Assert.Empty(fake.Transactions.Calls);
    }

    [Fact]
    public void AnotherTextChangesAfterApproval_FingerprintRejectsBeforeWriting()
    {
        var fake = new Mover(90);
        string approved = (string)fake.Plan()["fingerprint"]!;
        fake.Scene.Page.Placements[2].Properties["angle_deg"] = "90";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => fake.Execute(approved)).Error.Reason);
        Assert.Empty(fake.Transactions.Calls);
    }

    private sealed class Mover : IPlacementMover
    {
        internal ConnectionCreatePlanner.Scene Scene { get; private set; }
        internal DrawingPlacement Child => Scene.Page.Placements[1];
        internal JObject Request { get; }
        internal FakeTransactions Transactions { get; } = new();
        internal string? Failure { get; init; }
        internal Mover(int angle)
        {
            var fixture = new MoveFixture();
            Scene = fixture.Scene;
            foreach ((char digest, int slot) in new[] { ('b', 0), ('c', 1) })
            {
                DrawingPlacement text = fixture.Child(digest, [22, 22]);
                text.Bounds = [22, 22, 30, 24];
                text.Properties = new() { ["grouped"] = "false", ["property_id"] = "20214", ["property_index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture), ["angle_deg"] = "0", ["docked"] = "false" };
            }
            Request = new()
            {
                ["project"] = "Line1",
                ["page"] = "P",
                ["snap_to_grid"] = false,
                ["property_moves"] = new JArray(new JObject
                { ["parent"] = BridgeJson.FromObject(fixture.Root.Locator), ["property_id"] = 20214, ["property_index"] = 0, ["x_mm"] = 50, ["y_mm"] = 50, ["angle_deg"] = angle })
            };
            Transactions.Snapshot = () => { ConnectionCreatePlanner.Scene saved = Clone(); return () => Scene = saved; };
        }
        private ConnectionCreatePlanner.Scene Clone() => BridgeJson.FromObject(Scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
        public ConnectionCreatePlanner.Scene Read(string project, string page)
        {
            ConnectionCreatePlanner.Scene result = Clone();
            if (Failure == "committed" && Transactions.Calls.Contains("Commit") && !Transactions.Calls.Contains("Rollback"))
            { result.Page.Placements[1].Location[0]++; }
            return result;
        }
        public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
        {
            Child.Location = moves[0]!["after"]!.Values<double>().ToArray();
            Child.Bounds = moves[0]!["bounds"]!.Values<double>().ToArray();
            Child.Properties["angle_deg"] = moves[0]!["angle"]!.ToString();
            Child.Properties["docked"] = "false";
            if (Failure == "angle")
            { Child.Properties["angle_deg"] = "0"; }
            if (Failure == "position")
            { Child.Location[0]++; }
            if (Failure == "sibling")
            { Scene.Page.Placements[2].Location[0]++; }
            if (Failure == "parent")
            { Scene.Page.Placements[0].Location[0]++; }
            if (Failure == "logical")
            { Scene.Logical.Add(new() { StartDevice = "unplanned" }); }
            if (Failure == "cancel")
            { OperationContext.Current.Cancel(); }
        };
        internal JObject Plan() => PlacementMovePlanner.Plan(Request.ToString(), Scene);
        internal JObject Execute(string? fingerprint = null) => Assert.IsType<JObject>(new OperationContext("property-move", true, null).Run(() => PlacementMoveExecution.Execute(this, Transactions, Request, fingerprint ?? (string)Plan()["fingerprint"]!)));
    }
}

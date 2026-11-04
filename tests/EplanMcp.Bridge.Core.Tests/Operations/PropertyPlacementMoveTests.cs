using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyPlacementMoveTests
{
    [Fact]
    public void PropertyMove_LiveW4BoundsAcross248Wire_CommitsWithADeviationWarning()
    {
        var fake = new Mover(0) { ReadbackDigest = new string('f', 64) };
        fake.Scene.Page.PageSize = [420, 297];
        fake.Scene.Page.GridSize = 4;
        fake.Scene.Graphical = [[[248, 64], [248, 120]]];
        fake.Child.Location = [312, 92];
        fake.Child.Bounds = [263.12, 96.28, 312, 100.56];
        JToken move = fake.Request["property_moves"]![0]!;
        move["x_mm"] = 272;
        move["y_mm"] = 92;
        // ADR-0046: the measured crossing stays in the project as a reported deviation.
        JObject result = fake.Execute();

        Assert.Contains("Readback deviation: R3: Text bounds conflict with a line.", (string?)result["warning"], StringComparison.Ordinal);
        Assert.Contains("Commit", fake.Transactions.Calls);
    }

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

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void CreatedCableText_StaleBoundsOffset_CommitsAndReturnsMeasuredBounds(int beforeAngle)
    {
        var fake = CableText(beforeAngle);
        JObject approved = fake.Plan();
        double[] predicted = approved["moves"]![0]!["bounds"]!.Values<double>().ToArray();
        double[] expected = [59.4425, 109.24, 63.72125, 160];
        Assert.All(predicted.Zip(expected), pair => Assert.Equal(pair.Second, pair.First, 10));
        Assert.Equal([68d, 160d], approved["moves"]![0]!["after"]!.Values<double>());
        Assert.Equal(90, (int)approved["moves"]![0]!["angle"]!);
        string siblings = BridgeJson.FromObject(fake.Scene.Page.Placements.Where(p => p != fake.Child)).ToString();

        JObject result = fake.Execute((string)approved["fingerprint"]!);

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([63.72125, 109.24, 68d, 160d], result["moves"]![0]!["bounds"]!.Values<double>());
        Assert.Equal(fake.Child.Bounds, result["moves"]![0]!["bounds"]!.Values<double>());
        Assert.Equal([68d, 160d], fake.Child.Location);
        Assert.Equal("90", fake.Child.Properties["angle_deg"]);
        Assert.Equal("false", fake.Child.Properties["docked"]);
        Assert.Equal(siblings, BridgeJson.FromObject(fake.Scene.Page.Placements.Where(p => p != fake.Child)).ToString());
        Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], fake.Transactions.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void RotatedText_OutlineSizeWithinTolerance_ReturnsCommittedMeasurement(int angle)
    {
        double[] measured = angle is 90 or 270 ? [55, 60, 57.009, 68.009] : [55, 60, 63.009, 62.009];
        double[] committed = measured.Select((n, i) => n + (i % 2 == 0 ? 1 : 0)).ToArray();
        var fake = new Mover(angle) { ReadbackBounds = measured, CommittedBounds = committed };

        JObject result = fake.Execute();

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(committed, result["moves"]![0]!["bounds"]!.Values<double>());
        Assert.Equal([50d, 50d], fake.Child.Location);
        Assert.Equal(angle.ToString(System.Globalization.CultureInfo.InvariantCulture), fake.Child.Properties["angle_deg"]);
    }

    [Theory]
    [InlineData("angle")]
    [InlineData("position")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("text")]
    [InlineData("display")]
    [InlineData("property_id")]
    [InlineData("property_index")]
    [InlineData("docked")]
    [InlineData("text_parent")]
    [InlineData("sibling_bounds")]
    [InlineData("parent_bounds")]
    [InlineData("committed")]
    public void CreatedCableText_ChangedReadback_StillRollsBack(string failure)
    {
        var fake = CableText(90, failure);
        string before = BridgeJson.FromObject(fake.Scene).ToString();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;

        Assert.Equal(ErrorReason.EplanOperationFailed, error.Reason);
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(before, BridgeJson.FromObject(fake.Scene).ToString());
        Assert.DoesNotContain("CloseUndo", fake.Transactions.Calls);
        Assert.Contains("Rollback", fake.Transactions.Calls);
    }

    [Fact]
    public void TextOnTheLayersRotation_CanBeTurned()
    {
        var fake = new Mover(90);
        fake.Child.Properties["angle_deg"] = "from_layer";

        fake.Execute();

        Assert.Equal("90", fake.Child.Properties["angle_deg"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PropertyStyle_AllFieldsReadBackAndHiddenReadbackException(bool visible)
    {
        var fake = new Mover(90) { VisibleReadback = true };
        JObject move = (JObject)fake.Request["property_moves"]![0]!;
        move["alignment"] = "MiddleCenter";
        move["height_mm"] = -16002;
        move["visible"] = visible;
        move["layer"] = 3;
        move["pen"] = new JObject { ["color"] = -16002, ["width_mm"] = -16002, ["style"] = -16002, ["pattern_length"] = -16002, ["line_end"] = 2 };
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal("MiddleCenter", fake.Child.Properties["alignment"]);
        Assert.Equal("from_layer", fake.Child.Properties["height_mm"]);
        Assert.Equal("-16002", fake.Child.Properties["height_raw_mm"]);
        Assert.Equal("3", fake.Child.Properties["layer"]);
        Assert.Equal("-16002", fake.Child.Properties["pen_pattern_length"]);
        if (!visible)
        { Assert.Contains("not compared", (string?)result["warning"], StringComparison.Ordinal); }
        Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], fake.Transactions.Calls);
    }
    [Theory]
    [InlineData("alignment", "MiddleLeft")]
    [InlineData("height_mm", 5)]
    [InlineData("layer", 3)]
    [InlineData("visible", true)]
    public void PropertyStyle_WrongReadbackRollsBack(string name, object value)
    {
        var fake = new Mover(90) { Failure = "style" };
        fake.Request["property_moves"]![0]![name] = JToken.FromObject(value);
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error.Execution);
        Assert.Contains("Rollback", fake.Transactions.Calls);
    }
    [Theory]
    [InlineData("height_mm", 0)]
    [InlineData("height_mm", -16001)]
    [InlineData("visible", "false")]
    [InlineData("layer", -1)]
    [InlineData("layer", 1.5)]
    [InlineData("pen", "invalid")]
    public void PropertyStyle_InvalidFieldsRefuseBeforeWrite(string field, object value)
    {
        var fake = new Mover(90);
        fake.Request["property_moves"]![0]![field] = JToken.FromObject(value);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => fake.Plan()).Error.Reason);
        Assert.Empty(fake.Transactions.Calls);
    }

    private static Mover CableText(int beforeAngle, string? failure = null)
    {
        var fake = new Mover(90) { Failure = failure, ReadbackBounds = [63.72125, 109.24, 68, 160] };
        fake.Scene.Page.PageSize = [420, 297];
        fake.Child.Location = [60, 140];
        // The logs show the docked 0-degree outline; the 90-degree case isolates a translation with no rotation.
        fake.Child.Bounds = beforeAngle == 0 ? [9.240000000000002, 144.27875, 60, 148.5575] : [51.4425, 89.24, 55.72125, 140];
        fake.Child.Properties = new()
        {
            ["grouped"] = "false",
            ["property_id"] = "20040",
            ["property_index"] = "4",
            ["angle_deg"] = beforeAngle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["docked"] = beforeAngle == 0 ? "true" : "false",
            ["text"] = "??_??@ÖLFLEX CLASSIC 100 300/500V;",
            ["display"] = "ÖLFLEX CLASSIC 100 300/500V"
        };
        JToken move = fake.Request["property_moves"]![0]!;
        move["property_id"] = 20040;
        move["property_index"] = 4;
        move["x_mm"] = 68;
        move["y_mm"] = 160;
        return fake;
    }

    private sealed class Mover : IPlacementMover
    {
        internal ConnectionCreatePlanner.Scene Scene { get; private set; }
        internal DrawingPlacement Child => Scene.Page.Placements[1];
        internal JObject Request { get; }
        internal FakeTransactions Transactions { get; } = new();
        internal string? Failure { get; init; }
        internal bool? VisibleReadback { get; init; }
        internal double[]? ReadbackBounds { get; init; }
        internal double[]? CommittedBounds { get; init; }
        internal string? ReadbackDigest { get; init; }
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
            if (Transactions.Calls.Contains("Commit") && !Transactions.Calls.Contains("Rollback"))
            {
                if (Failure == "committed")
                { result.Page.Placements[1].Location[0]++; }
                if (CommittedBounds is not null)
                { result.Page.Placements[1].Bounds = CommittedBounds.ToArray(); }
            }
            return result;
        }
        public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
        {
            Child.Location = moves[0]!["after"]!.Values<double>().ToArray();
            Child.Bounds = ReadbackBounds?.ToArray() ?? moves[0]!["bounds"]!.Values<double>().ToArray();
            Child.Properties["angle_deg"] = moves[0]!["angle"]!.ToString();
            Child.Properties["docked"] = "false";
            if (Failure != "style" && moves[0]!["style"] is JObject style)
            { foreach (var pair in PropertyPlacementMoves.StyleValues(style)) { Child.Properties[pair.Key] = pair.Value; } }
            if (VisibleReadback is { } visible)
            { Child.Properties["visible"] = visible.ToString().ToLowerInvariant(); }
            if (ReadbackDigest is not null)
            { Child.Locator.Digest = ReadbackDigest; }
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
            if (Failure == "width")
            { Child.Bounds[2] += 0.02; }
            if (Failure == "height")
            { Child.Bounds[3] += 0.02; }
            if (Failure is "text" or "display" or "property_id" or "property_index" or "docked")
            { Child.Properties[Failure] = "changed"; }
            if (Failure == "text_parent")
            { Child.Parent = new() { Project = "scratch-project", Page = "P", Type = 17, Oid = 999 }; }
            if (Failure == "sibling_bounds")
            { Scene.Page.Placements[2].Bounds![0]++; }
            if (Failure == "parent_bounds")
            { Scene.Page.Placements[0].Bounds![0]++; }
            if (Failure == "cancel")
            { OperationContext.Current.Cancel(); }
        };
        internal JObject Plan() => PlacementMovePlanner.Plan(Request.ToString(), Scene);
        internal JObject Execute(string? fingerprint = null) => Assert.IsType<JObject>(new OperationContext("property-move", true, null).Run(() => PlacementMoveExecution.Execute(this, Transactions, Request, fingerprint ?? (string)Plan()["fingerprint"]!)));
    }
}

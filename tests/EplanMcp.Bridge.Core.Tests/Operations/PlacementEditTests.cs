using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementEditTests
{
    [Theory]
    [InlineData(1, 60, 20, Math.PI / 2)]
    [InlineData(2, 60, 60, Math.PI)]
    [InlineData(-1, 20, 60, 3 * Math.PI / 2)]
    public void Rotation_PivotAndSignedQuarterTurns_ArePredicted(int turns, double x, double y, double angle)
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input();
        input["quarter_turns"] = turns;
        input["pivot_mm"] = new JArray(40, 40);
        JToken row = PlacementEditPlanner.Plan(input, fake.Data)["predicted"]![0]!;
        Assert.Equal([x, y], row["location"]!.Values<double>());
        Assert.Equal(angle, (double)row["angle"]!, 8);
    }
    [Theory]
    [InlineData("horizontal", 20, 100, 0)]
    [InlineData("vertical", 100, 20, Math.PI)]
    public void Mirror_ReflectsPositionAndTextAngle(string axis, double x, double y, double angle)
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("mirror");
        input["axis"] = axis;
        input["coordinate_mm"] = 60;
        JToken row = PlacementEditPlanner.Plan(input, fake.Data)["predicted"]![0]!;
        Assert.Equal([x, y], row["location"]!.Values<double>());
        Assert.Equal(angle, (double)row["angle"]!, 8);
    }
    [Theory]
    [InlineData("left", 0, 20)]
    [InlineData("right", 0, 20)]
    [InlineData("centre_x", 0, 20)]
    [InlineData("top", 1, 20)]
    [InlineData("bottom", 1, 20)]
    [InlineData("centre_y", 1, 20)]
    public void Alignment_UsesTheFirstRequestedObject(string alignment, int axis, double coordinate)
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("align", 0, 1);
        input["alignment"] = alignment;
        JObject plan = PlacementEditPlanner.Plan(input, fake.Data);
        Assert.Equal(coordinate, (double)plan["predicted"]![1]!["location"]![axis]!);
    }
    [Fact]
    public void Distribution_UsesEqualOutlineGapsAndFixedExtremes()
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("distribute", 2, 0, 1);
        input["axis"] = "horizontal";
        JObject plan = PlacementEditPlanner.Plan(input, fake.Data);
        Assert.Equal([120d, 20d, 70d], plan["predicted"]!.Select(p => (double)p["location"]![0]!));
    }
    [Fact]
    public void Copy_TargetPointAndStep_PreserveLayoutAndNames()
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("copy", 0, 1);
        input["target_mm"] = new JArray(200, 100);
        input["copies"] = 2;
        input["step_mm"] = new JArray(40, 0);
        JToken rows = PlacementEditPlanner.Plan(input, fake.Data)["predicted"]!;
        Assert.Equal([200d, 240d, 230d, 270d], rows.Select(r => (double)r["location"]![0]!));
        Assert.All(rows, row => Assert.Equal("??_??@note;", (string?)row["properties"]!["text"]));
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    [InlineData("foreign")]
    [InlineData("children")]
    [InlineData("copies")]
    [InlineData("turns")]
    [InlineData("unrelated")]
    [InlineData("offpage")]
    public void InvalidOrUnsafeEvidence_FailsBeforeWriting(string scenario)
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input();
        input["quarter_turns"] = 1;
        switch (scenario)
        {
            case "unknown":
                input["operation"] = "scale";
                break;
            case "duplicate":
                ((JArray)input["locators"]!).Add(input["locators"]![0]!.DeepClone());
                break;
            case "stale":
                input["locators"]![0]!["digest"] = new string('b', 64);
                break;
            case "foreign":
                input["locators"]![0]!["project"] = "other";
                break;
            case "children":
                fake.Data["objects"]![0]!["kind"] = "PropertyPlacement";
                break;
            case "copies":
                input["copies"] = 21;
                break;
            case "turns":
                input["quarter_turns"] = 0;
                break;
            case "unrelated":
                input["alignment"] = "left";
                break;
            case "offpage":
                input["pivot_mm"] = new JArray(300, 300);
                break;
        }
        Assert.Throws<BridgeErrorException>(() => PlacementEditPlanner.Plan(input, fake.Data));
        Assert.Equal(0, fake.Transactions);
    }
    [Theory]
    [InlineData(false, false, ExecutionState.Committed)]
    [InlineData(true, false, ExecutionState.RolledBack)]
    [InlineData(true, true, ExecutionState.Unknown)]
    public void ReadbackAndRollback_ProveTheReportedOutcome(bool mismatch, bool brokenRollback, string expected)
    {
        var fake = new PlacementEditFixture { FailRollback = brokenRollback };
        JObject input = fake.Input();
        input["quarter_turns"] = 1;
        input["pivot_mm"] = new JArray(40, 40);
        string hash = (string)PlacementEditPlanner.Plan(input, fake.Data)["fingerprint"]!;
        fake.Mutation = () => { JToken root = fake.Data["objects"]![0]!; root["location"] = new JArray(mismatch ? 61 : 60, 20); root["bounds"] = new JArray(59, 19, 61, 21); root["angle"] = Math.PI / 2; };
        var operation = new PlacementEditExecuteOperation(fake, fake, fake.Backup);
        if (mismatch)
        { BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new JObject { ["request"] = input, ["fingerprint"] = hash })); Assert.Equal(expected, error.Error.Execution); }
        else
        { JObject result = (JObject)operation.Execute(new JObject { ["request"] = input, ["fingerprint"] = hash })!; Assert.Equal(expected, (string?)result["execution"]); }
        Assert.Equal(1, fake.Transactions);
        Assert.Equal(mismatch ? 0 : 1, fake.Closed);
    }
    [Fact]
    public void Delete_ReportsOnlyConnectionsIncidentToRemovedFunctions()
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("delete");
        fake.Data["connections"] = new JArray(new JObject { ["type"] = 7, ["oid"] = 10, ["start"] = new JObject { ["type"] = 4, ["oid"] = 1 }, ["end"] = new JObject { ["type"] = 4, ["oid"] = 2 } });
        Assert.Single(PlacementEditPlanner.Plan(input, fake.Data)["removedConnections"]!);
    }
    [Fact]
    public void Copy_ReadbackReturnsFreshFindLocatorsOnceAcrossBothVerificationPhases()
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("copy");
        input["offset_mm"] = new JArray(60, 60);
        string hash = (string)PlacementEditPlanner.Plan(input, fake.Data)["fingerprint"]!;
        fake.Mutation = () =>
        {
            JObject copy = (JObject)fake.Data["objects"]![0]!.DeepClone();
            copy["locator"]!["oid"] = 4;
            copy["drawing"]!["oid"] = 4;
            copy["location"] = new JArray(80, 80);
            copy["bounds"] = new JArray(79, 79, 81, 81);
            ((JArray)fake.Data["objects"]!).Add(copy);
        };
        JObject result = (JObject)new PlacementEditExecuteOperation(fake, fake, fake.Backup).Execute(new JObject { ["request"] = input, ["fingerprint"] = hash })!;
        Assert.Equal(4, Assert.Single(result["newLocators"]!)["oid"]!.Value<int>());
        Assert.Equal(0, fake.Backups);
        Assert.Equal(1, fake.Closed);
    }
    [Fact]
    public void CancellationAfterFirstMutation_RollsBackWithoutAnEmptyStep()
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("delete", 0, 1);
        string hash = (string)PlacementEditPlanner.Plan(input, fake.Data)["fingerprint"]!, original = fake.Data.ToString();
        var context = new OperationContext("edit-cancel", true, null);
        fake.Mutation = () => { fake.Data["objects"]![0]!.Remove(); context.Cancel(); context.ThrowIfCancellationRequested(); };
        var execute = new PlacementEditExecuteOperation(fake, fake, (_, _) => { });
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => context.Run(() => execute.Execute(new JObject { ["request"] = input, ["fingerprint"] = hash, ["backup"] = new JObject() })));
        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Equal(original, fake.Data.ToString());
        Assert.Equal(1, fake.Transactions);
        Assert.Equal(0, fake.Closed);
    }
    [Fact]
    public void CompleteMultilingualImpact_ExceedingConciseBudget_RequiresDetailedBeforeAnyWrite()
    {
        var fake = new PlacementEditFixture();
        fake.Data["objects"]![0]!["properties"]!["text"] = new string('\u4e00', 6000);
        JObject input = fake.Input("copy");
        input["offset_mm"] = new JArray(60, 60);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => PlacementEditPlanner.Plan(input, fake.Data)).Error.Reason);
        input["response_format"] = "detailed";
        Assert.Single(PlacementEditPlanner.Plan(input, fake.Data)["predicted"]!);
        Assert.Equal(0, fake.Transactions);
    }
    [Fact]
    public void UninventoriedPlacementSideEffect_RollsBackEvenWhenSelectedGeometryMatches()
    {
        var fake = new PlacementEditFixture();
        JObject input = fake.Input("delete");
        string hash = (string)PlacementEditPlanner.Plan(input, fake.Data)["fingerprint"]!, baseline = fake.Data.ToString();
        fake.Mutation = () => { fake.Data["objects"]![0]!.Remove(); fake.Data["scene"]!["complete"] = false; };
        var execute = new PlacementEditExecuteOperation(fake, fake, (_, _) => { });
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => execute.Execute(new JObject { ["request"] = input, ["fingerprint"] = hash, ["backup"] = new JObject() }));
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Equal(baseline, fake.Data.ToString());
        Assert.Equal(0, fake.Closed);
    }
}

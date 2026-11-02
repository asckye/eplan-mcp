using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementMoveWireAnnotationTests
{
    [Fact]
    public void LiveCable_TrunkToBranchBySixtyFour_IsAllowedWithoutChangingAutoconnect()
    {
        var fixture = new Fixture("Cable");
        JObject plan = fixture.Plan();
        Assert.Equal(ExecutionState.NotStarted, (string?)plan["execution"]);
        Assert.Equal([80d, 192d], plan["moves"]![0]!["after"]!.Values<double>());
        Assert.Equal([80d, 192d, 160d, 192d], plan["moves"]![0]!["bounds"]!.Values<double>());
        Assert.Empty(plan["graphicalChanges"]!);
        Assert.Empty(plan["connectionChanges"]!);
        Assert.Equal([80d, 128d, 160d, 128d], fixture.Target.Bounds!);
    }

    [Fact]
    public void ConnectionDefinitionPoint_AlongItsWire_IsAllowed()
    {
        var fixture = new Fixture("ConnectionDefinitionPoint");
        JObject plan = fixture.Plan();
        Assert.Equal([120d, 192d], plan["moves"]![0]!["after"]!.Values<double>());
        Assert.Equal([118.75, 190.75, 121.25, 193.25], plan["moves"]![0]!["bounds"]!.Values<double>());
        Assert.Empty(plan["graphicalChanges"]!);
    }

    [Theory]
    [InlineData("Function")]
    [InlineData("SymbolReference")]
    [InlineData("Text")]
    [InlineData("Line")]
    [InlineData("Rectangle")]
    public void OtherKinds_UnknownOutlineMovedOntoBranch_StillRefuseWithSameMessage(string kind)
    {
        var fixture = new Fixture(kind);
        fixture.Target.Bounds = null;
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, failure.Reason);
        Assert.Equal(ExecutionState.NotStarted, failure.Execution);
        Assert.Equal("/moves: A moved outline conflicts with an autoconnect line.", failure.Metadata!["violations"]);
    }

    [Theory]
    [InlineData("Function", "moved outline conflicts with an autoconnect line.")]
    [InlineData("SymbolReference", "moved outline conflicts with an autoconnect line.")]
    [InlineData("Text", "Text bounds conflict with a line.")]
    [InlineData("Line", "A moved outline conflicts with an autoconnect line.")]
    [InlineData("Rectangle", "A moved outline conflicts with an autoconnect line.")]
    public void OtherKinds_MeasuredOutlineMovedOntoBranch_StillRefuse(string kind, string message)
    {
        var fixture = new Fixture(kind);
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, failure.Reason);
        Assert.Equal(ExecutionState.NotStarted, failure.Execution);
        Assert.Contains(message, failure.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void LiveCable_MeasuredAfterMove_DoesNotBecomeAnR4Symbol()
    {
        var fixture = new Fixture("Cable");
        LayoutGuard.Model before = LayoutGuard.From(fixture.Scene);
        var measuredScene = BridgeJson.FromObject(fixture.Scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
        measuredScene.Page.Placements[^1] = LayoutGuard.Shift(fixture.Target, [0, 64]);
        LayoutGuard.Model measured = LayoutGuard.From(measuredScene);
        Assert.Contains(LayoutGuard.AfterWriting(before, measured), s => s.StartsWith("Layout after write: R1-R9", StringComparison.Ordinal));
        Assert.Empty(LayoutGuard.Check(measured, [measuredScene.Page.Placements[^1]], suggest: false, measured: true));

        measuredScene.Page.Placements[^1].Kind = "Function";
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => LayoutGuard.AfterWriting(before, measured)).Error;
        Assert.Contains("R4:", failure.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cable")]
    [InlineData("ConnectionDefinitionPoint")]
    public void WireAnnotations_OutsidePageBounds_StillRefuse(string kind)
    {
        var fixture = new Fixture(kind);
        fixture.Request["moves"]![0]!["delta_mm"] = new JArray(0, 192);
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, failure.Reason);
        Assert.Equal(ExecutionState.NotStarted, failure.Execution);
        Assert.Contains("Keep every following outline inside Page.Size", failure.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cable")]
    [InlineData("ConnectionDefinitionPoint")]
    public void WireAnnotations_PinsOutsidePageBounds_StillRefuse(string kind)
    {
        var fixture = new Fixture(kind);
        fixture.Scene.Pins["17/69842/0"].Location = [120, 300];
        fixture.Scene.Graphical[1] = [[120, 160], [120, 300]];
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, failure.Reason);
        Assert.Equal(ExecutionState.NotStarted, failure.Execution);
        Assert.Equal("/moves: Keep all following pins inside Page.Size.", failure.Metadata!["violations"]);
    }

    private sealed class Fixture
    {
        internal ConnectionCreatePlanner.Scene Scene { get; } = TNodeFixture.Scene();
        internal DrawingPlacement Target { get; }
        internal JObject Request { get; }

        internal Fixture(string kind)
        {
            Scene.Complete = true;
            Scene.Project = new() { Name = "Line1", Path = "scratch-project" };
            Scene.Page.Project = Scene.Project.Path;
            Target = new()
            {
                Kind = kind,
                Locator = new() { Type = kind == "Cable" ? 10005 : kind == "ConnectionDefinitionPoint" ? 59 : 17, Oid = 70000 },
                Location = kind == "Cable" ? [80, 128] : [120, 128],
                Bounds = kind == "Cable" ? [80, 128, 160, 128] : [118.75, 126.75, 121.25, 129.25]
            };
            Scene.Page.Placements.Add(Target);
            foreach (DrawingPlacement placement in Scene.Page.Placements)
            {
                placement.Locator.Project = Scene.Project.Path;
                placement.Locator.Page = Scene.Page.Page;
                placement.Locator.Digest = new string('a', 64);
                placement.Properties["grouped"] = "false";
                placement.Bounds ??= [placement.Location[0] - 1, placement.Location[1] - 1, placement.Location[0] + 1, placement.Location[1] + 1];
            }
            if (kind == "ConnectionDefinitionPoint")
            { Scene.PointRoutes["59/70000"] = [Scene.Graphical[0], Scene.Graphical[1]]; }
            Request = new()
            {
                ["project"] = Scene.Project.Name, ["page"] = Scene.Page.Page, ["dry_run"] = true,
                ["moves"] = new JArray(MoveFixture.Move(Target.Locator, [0, 64]))
            };
        }

        internal JObject Plan() => PlacementMovePlanner.Plan(Request.ToString(), Scene);
    }
}

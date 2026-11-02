using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionMoveReadbackTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("location")]
    [InlineData("bounds")]
    [InlineData("name")]
    [InlineData("parent")]
    [InlineData("different-name")]
    [InlineData("writable-text")]
    [InlineData("missing-display")]
    public void PairingFixture_MoveSecondPoint_AllowsOnlyComputedBlockTextOnItsNamedGroup(string? failure)
    {
        var mover = new Mover(failure);
        string before = BridgeJson.FromObject(mover.Scene).ToString();
        string fingerprint = (string)PlacementMovePlanner.Plan(mover.Input.ToString(), mover.Scene)["fingerprint"]!;
        object? Execute() => new OperationContext("pairing", true, null).Run(() => PlacementMoveExecution.Execute(mover, mover.Transactions, mover.Input, fingerprint));
        if (failure is null)
        {
            JObject result = Assert.IsType<JObject>(Execute());
            Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
            Assert.Equal([400d, 160d], mover.Scene.Page.Placements[1].Location);
            DrawingPlacement display = mover.Scene.Page.Placements[4];
            Assert.Equal([84d, 160d], display.Location);
            Assert.Equal("??_??@-E103-/5;", display.Properties["text"]);
            Assert.Equal("-E103-/5", display.Properties["display"]);
            Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], mover.Transactions.Calls);
        }
        else
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(Execute).Error;
            Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
            Assert.Equal(before, BridgeJson.FromObject(mover.Scene).ToString());
            Assert.DoesNotContain("CloseUndo", mover.Transactions.Calls);
        }
    }

    private sealed class Mover : IPlacementMover
    {
        internal ConnectionCreatePlanner.Scene Scene { get; private set; } = new()
        {
            Complete = true,
            Project = new() { Name = "LargeWrite", Path = "scratch-project" },
            Page = new() { Project = "scratch-project", Page = "=GAA+A1&EFS1/103", GridSize = 4, PageSize = [420, 297] },
        };
        internal FakeTransactions Transactions { get; } = new();
        internal JObject Input { get; }
        private readonly string? _failure;
        internal Mover(string? failure)
        {
            _failure = failure;
            for (int i = 0; i < 4; i++)
            {
                var root = new DrawingPlacement
                {
                    Locator = new() { Project = Scene.Project.Path, Page = Scene.Page.Page, Type = 70, Oid = 2265 + i, Digest = new string((char)('a' + i), 64) },
                    Kind = "InterruptionPoint",
                    Location = [80 * (i + 1), 160],
                    Bounds = [80 * (i + 1) - 1, 159, 80 * (i + 1) + 1, 161],
                    Properties = new() { ["grouped"] = "false", ["name"] = "=GAA+A1-R103I1" },
                };
                Scene.Page.Placements.Add(root);
            }
            DrawingPlacement owner = Scene.Page.Placements[0];
            Scene.Page.Placements.Add(new()
            {
                Locator = new() { Project = Scene.Project.Path, Page = Scene.Page.Page, Type = 45, Oid = 1963, Digest = new string('f', 64) },
                Parent = owner.Locator,
                Kind = "PropertyPlacement",
                Location = [84, 160],
                Bounds = [0, 0, 0, 0],
                Properties = new() { ["grouped"] = "false", ["displayed_property_id"] = failure == "writable-text" ? "20011" : "20201", ["text"] = "??_??@-E103-/3;", ["display"] = "-E103-/3" },
            });
            if (failure == "different-name")
            { owner.Properties["name"] = "=GAA+A1-Other"; }
            Input = new() { ["project"] = Scene.Project.Name, ["page"] = Scene.Page.Page, ["moves"] = new JArray(new JObject { ["locator"] = BridgeJson.FromObject(Scene.Page.Placements[1].Locator), ["x_mm"] = 400, ["y_mm"] = 160 }) };
            Transactions.Snapshot = () => { var saved = Clone(Scene); return () => Scene = saved; };
        }
        public ConnectionCreatePlanner.Scene Read(string project, string page) => Clone(Scene);
        public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
        {
            Scene.Page.Placements[1].Location = [400, 160];
            Scene.Page.Placements[1].Bounds = [399, 159, 401, 161];
            DrawingPlacement display = Scene.Page.Placements[4];
            display.Locator.Digest = new string('e', 64);
            display.Properties["text"] = "??_??@-E103-/5;";
            display.Properties["display"] = "-E103-/5";
            switch (_failure)
            {
                case "location":
                    display.Location = [88, 160];
                    break;
                case "bounds":
                    display.Bounds = [80, 160, 88, 164];
                    break;
                case "name":
                    Scene.Page.Placements[0].Properties["name"] = "changed";
                    break;
                case "parent":
                    display.Parent = Scene.Page.Placements[2].Locator;
                    break;
                case "missing-display":
                    display.Properties.Remove("display");
                    break;
            }
        };
        private static ConnectionCreatePlanner.Scene Clone(ConnectionCreatePlanner.Scene scene) => BridgeJson.FromObject(scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
    }
}

using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementMoveCablePointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveCable_RecreatedCrossingPoint_CommitsWithNewIdentity(bool parentedPoint)
    {
        var mover = new Mover();
        if (parentedPoint)
        { mover.Scene.Page.Placements.Single(p => p.Kind == "ConnectionDefinitionPoint").Parent = mover.Cable.Locator; }
        JObject plan = mover.Plan();
        Assert.DoesNotContain(plan["moves"]!, r => (int)r["locator"]!["type"]! == 59);

        JObject result = mover.Execute();

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal([80d, 192d, 160d, 192d], mover.Cable.Bounds!);
        DrawingPlacement point = Assert.Single(mover.Scene.Page.Placements, p => p.Kind == "ConnectionDefinitionPoint");
        Assert.Equal((59, 69846L), (point.Locator.Type, point.Locator.Oid));
        Assert.Equal([120d, 192d], point.Location);
        Assert.Equal("+A1-WReview103", point.Properties["name"]);
        Assert.Equal(["Begin AI: eplan_placement_update", "Commit", "CloseUndo", "Dispose"], mover.Transactions.Calls);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("old_identity", false)]
    [InlineData("old_crossing", false)]
    [InlineData("wrong_cable", false)]
    [InlineData("duplicate", false)]
    [InlineData("text", false)]
    [InlineData("line", false)]
    [InlineData("missing", true)]
    [InlineData("old_identity", true)]
    [InlineData("old_crossing", true)]
    [InlineData("text", true)]
    public void LiveCable_CrossingOrClosureMismatch_RollsBackWholeScene(string failure, bool afterCommit)
    {
        var mover = new Mover { Failure = failure, AfterCommit = afterCommit };
        string before = BridgeJson.FromObject(mover.Scene).ToString();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute()).Error;

        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(before, BridgeJson.FromObject(mover.Scene).ToString());
        Assert.Contains("Rollback", mover.Transactions.Calls);
        Assert.DoesNotContain("CloseUndo", mover.Transactions.Calls);
        Assert.Equal(afterCommit ? 1 : 0, mover.Transactions.Calls.Count(c => c == "Commit"));
    }

    [Fact]
    public void DefinitionPointMovedAlone_RecreatedIdentity_StillRollsBack()
    {
        var mover = new Mover();
        DrawingPlacement point = mover.Scene.Page.Placements.Single(p => p.Kind == "ConnectionDefinitionPoint");
        mover.Scene.PointRoutes["59/69845"] = [mover.Scene.Graphical[0], mover.Scene.Graphical[1]];
        mover.Request["moves"] = new JArray(MoveFixture.Move(point.Locator, [0, 64]));
        string before = BridgeJson.FromObject(mover.Scene).ToString();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute()).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(before, BridgeJson.FromObject(mover.Scene).ToString());
    }

    private sealed class Mover : IPlacementMover
    {
        internal ConnectionCreatePlanner.Scene Scene { get; private set; } = TNodeFixture.Scene();
        internal FakeTransactions Transactions { get; } = new();
        internal string? Failure { get; init; }
        internal bool AfterCommit { get; init; }
        internal DrawingPlacement Cable => Scene.Page.Placements.Single(p => p.Kind == "Cable");
        internal JObject Request { get; }
        private bool _moved;

        internal Mover()
        {
            Scene.Complete = true;
            Scene.Project = new() { Name = "LargeWrite", Path = "scratch-project" };
            Scene.Page.Project = Scene.Project.Path;
            Scene.Page.Placements.Add(new()
            {
                Kind = "Cable",
                Locator = new() { Type = 10005, Oid = 70000 },
                Location = [80, 128],
                Bounds = [80, 128, 160, 128],
                Properties = new() { ["name"] = "+A1-WReview103" }
            });
            Scene.Page.Placements.Add(Point(69845, 128));
            Scene.Page.Placements.Add(new()
            {
                Kind = "Text",
                Locator = new() { Type = 45, Oid = 90000 },
                Location = [280, 240],
                Bounds = [280, 240, 288, 244],
                Properties = new() { ["text"] = "Unrelated text", ["height_mm"] = "3.5", ["angle"] = "0" }
            });
            foreach (DrawingPlacement placement in Scene.Page.Placements)
            {
                placement.Locator.Project = Scene.Project.Path;
                placement.Locator.Page = Scene.Page.Page;
                placement.Locator.Digest = new string('a', 64);
                placement.Properties["grouped"] = "false";
                placement.Bounds ??= [placement.Location[0] - 1, placement.Location[1] - 1, placement.Location[0] + 1, placement.Location[1] + 1];
            }
            Scene.PointRoutes["59/69845"] = [Scene.Graphical[0]];
            Scene.Logical = [new() { StartDevice = "17/69842/0", EndDevice = "17/69843/0" }];
            Request = new()
            {
                ["project"] = Scene.Project.Name,
                ["page"] = Scene.Page.Page,
                ["moves"] = new JArray(MoveFixture.Move(Cable.Locator, [0, 64]))
            };
            Transactions.Snapshot = () =>
            {
                ConnectionCreatePlanner.Scene saved = Clone(Scene);
                return () => { Scene = saved; _moved = false; };
            };
        }

        private static DrawingPlacement Point(long oid, double y) => new()
        {
            Kind = "ConnectionDefinitionPoint",
            Locator = new() { Project = "scratch-project", Page = "=GAA+A1&EFS1/103", Type = 59, Oid = oid, Digest = new string('b', 64) },
            Location = [120, y],
            Bounds = [118.75, y - 1.25, 121.25, y + 1.25],
            Properties = new() { ["name"] = "+A1-WReview103", ["grouped"] = "false" }
        };

        public ConnectionCreatePlanner.Scene Read(string project, string page)
        {
            ConnectionCreatePlanner.Scene copy = Clone(Scene);
            if (_moved && AfterCommit && Transactions.Calls.Contains("Commit"))
            { Inject(copy); }
            return copy;
        }

        public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
        {
            foreach (JToken row in moves)
            {
                DrawingPlacement placement = Scene.Page.Placements.Single(p => DrawingLocators.Matches(p.Locator, row["locator"]!.ToObject<DrawingLocator>(BridgeJson.Serializer)!));
                placement.Location = row["after"]!.Values<double>().ToArray();
                placement.Bounds = row["bounds"]!.Values<double>().ToArray();
                placement.Locator.Digest = new string('c', 64);
            }
            Scene.Page.Placements.RemoveAll(p => p.Kind == "ConnectionDefinitionPoint");
            Scene.Page.Placements.Add(Point(69846, 192));
            Scene.PointRoutes.Clear();
            Scene.PointRoutes["59/69846"] = [Scene.Graphical[1]];
            _moved = true;
            if (!AfterCommit)
            { Inject(Scene); }
        };

        private void Inject(ConnectionCreatePlanner.Scene scene)
        {
            DrawingPlacement point = scene.Page.Placements.Single(p => p.Kind == "ConnectionDefinitionPoint");
            switch (Failure)
            {
                case "missing":
                    scene.Page.Placements.Remove(point);
                    break;
                case "old_identity":
                    point.Locator.Oid = 69845;
                    break;
                case "old_crossing":
                    scene.Page.Placements.Insert(4, Point(69847, 128));
                    break;
                case "wrong_cable":
                    point.Properties["name"] = "+A1-Unrelated";
                    break;
                case "duplicate":
                    scene.Page.Placements.Add(Point(69847, 192));
                    break;
                case "text":
                    scene.Page.Placements.Single(p => p.Kind == "Text").Location[0] += 4;
                    break;
                case "line":
                    scene.Page.Placements.Single(p => p.Kind == "Cable").Bounds![3] += 4;
                    break;
            }
        }

        internal JObject Plan() => PlacementMovePlanner.Plan(Request.ToString(), Scene);
        internal JObject Execute() => Assert.IsType<JObject>(new OperationContext("cable-move", true, null).Run(() => PlacementMoveExecution.Execute(this, Transactions, Request, (string)Plan()["fingerprint"]!)));
        private static ConnectionCreatePlanner.Scene Clone(ConnectionCreatePlanner.Scene scene) => BridgeJson.FromObject(scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
    }
}

using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Project-owned fake placements throw if the adapter disposes them (A39).

[Trait("Size", "Small")]
public sealed class PlacementMoverTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepare_ReadOnlyRootOrProject_RefusesBeforeMoveAndTransaction(bool objectReadOnly)
    {
        var text = new Text(2) { Location = new(40, 40), IsReadOnly = objectReadOnly };
        var page = new Page { AllPlacements = [text] };
        EplanProjects.Project = new Project(page) { IsReadOnly = !objectReadOnly };
        text.Project = EplanProjects.Project;
        var transactions = new FakeTransactions();
        try
        {
            var scene = new ConnectionCreatePlanner.Scene { Project = new() { Path = "project" }, Page = new() { Page = page.Name } };
            BridgeError error = Assert.Throws<BridgeErrorException>(() =>
            {
                Action move = new EplanPlacementMover().Prepare(scene, new JArray(Row(text, null, [50, 55])));
                TransactionRunner.Run(transactions, "eplan_placement_update", () => { move(); return true; });
            }).Error;
            Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
            Assert.Equal(new PointD(40, 40), text.Location);
            Assert.Empty(transactions.Calls);
            Assert.Equal(0, text.UndockCalls);
        }
        finally { EplanProjects.Project = null; }
    }

    [Fact]
    public void CableW9_DefaultDockedTexts_CopyOnceInsideWriteAndReacquireBeforeMoving()
    {
        // q-d46-CableDefinitionTests / 988b799cb3e24e9f9120a1890da6ac76: native library texts at (60,140).
        var owner = new Function(69847) { Name = "+A1-W9", Location = new(60, 140), UseLocalPropertyPlacements = false };
        owner.PropertyPlacements = [.. Enumerable.Range(0, 5).Select(i => new PropertyPlacement(5, owner)
        {
            IdentifierType = 45, Location = new(0, 0), IsDocked = true, ReadOnly = true,
            DisplayedId = i == 2 ? 20053 : i == 4 ? 20040 : 20214,
            Display = i == 2 ? "3x0.5" : i == 4 ? "ÖLFLEX CLASSIC 100 300/500V" : "",
            Bounds = i == 2 ? [new(50.6, 140), new(60, 144.27875)] : i == 4 ? [new(9.240000000000002, 144.27875), new(60, 148.5575)] : [new(0, 0), new(0, 0)],
        })];
        PropertyPlacement[] defaults = owner.PropertyPlacements;
        var page = new Page { Name = "=GAA+A1&EFS1/103", AllPlacements = [owner] };
        EplanProjects.Project = new(page);
        try
        {
            var reader = new EplanDrawingReader();
            PageDrawingGeometry before = reader.Read("project", page.Name)!;
            before.Project = "project";
            var scene = new ConnectionCreatePlanner.Scene { Project = new() { Path = "project" }, Page = before, Complete = true };
            JObject request = new()
            {
                ["project"] = "project",
                ["page"] = page.Name,
                ["snap_to_grid"] = false,
                ["response_format"] = "detailed",
                ["property_moves"] = new JArray(new[] { (Id: 20053, Slot: 2, X: 60), (Id: 20040, Slot: 4, X: 68) }.Select(p => new JObject
                {
                    ["parent"] = BridgeJson.FromObject(before.Placements[0].Locator),
                    ["property_id"] = p.Id,
                    ["property_index"] = p.Slot,
                    ["x_mm"] = p.X,
                    ["y_mm"] = 160,
                    ["angle_deg"] = 90,
                })),
            };
            JObject plan = PlacementMovePlanner.Plan(request.ToString(), scene);
            Action write = new EplanPlacementMover().Prepare(scene, (JArray)plan["moves"]!);
            Assert.False(owner.UseLocalPropertyPlacements);
            Assert.Same(defaults, owner.PropertyPlacements);
            bool writing = false;
            owner.BeforeLocalCopy = () => Assert.True(writing);
            writing = true;
            write();
            writing = false;
            Assert.True(owner.UseLocalPropertyPlacements);
            Assert.Equal(1, owner.LocalCopies);
            Assert.Equal(new PointD(0, 20), owner.PropertyPlacements[2].Location);
            Assert.Equal(new PointD(8, 20), owner.PropertyPlacements[4].Location);
            int[] movedSlots = [2, 4];
            Assert.All(movedSlots, i =>
            {
                Assert.Equal(Math.PI / 2, owner.PropertyPlacements[i].Rotation);
                Assert.False(owner.PropertyPlacements[i].IsDocked);
                Assert.True(defaults[i].IsDocked);
                Assert.Equal(new PointD(0, 0), defaults[i].Location);
            });
            PageDrawingGeometry after = reader.Read("project", page.Name)!;
            after.Project = "project";
            Assert.Equal(BridgeJson.FromObject(before.Placements[0]).ToString(), BridgeJson.FromObject(after.Placements[0]).ToString());
            foreach (int i in new[] { 0, 1, 3 })
            {
                Assert.NotSame(defaults[i], owner.PropertyPlacements[i]);
                Assert.Equal(BridgeJson.FromObject(before.Placements[i + 1]).ToString(), BridgeJson.FromObject(after.Placements[i + 1]).ToString());
            }
            foreach (int i in movedSlots)
            {
                Assert.Equal(before.Placements[i + 1].Locator.Oid, after.Placements[i + 1].Locator.Oid);
                Assert.NotEqual(before.Placements[i + 1].Locator.Digest, after.Placements[i + 1].Locator.Digest);
            }
            Assert.True((bool)PlacementMovePlanner.Plan(request.ToString(), new() { Project = scene.Project, Page = after, Complete = true })["noOp"]!);
        }
        finally { EplanProjects.Project = null; }
    }

    [Fact]
    public void DefaultTexts_SharedLibraryIdentifier_KeepEveryParentsSlotsAndStableCopyIdentity()
    {
        var first = new Function(69847) { UseLocalPropertyPlacements = false };
        var second = new Function(69848) { UseLocalPropertyPlacements = false };
        foreach (Function owner in new[] { first, second })
        {
            owner.PropertyPlacements = [new(5, owner) { IdentifierType = 45, DisplayedId = 20053, ReadOnly = true },
                new(5, owner) { IdentifierType = 45, DisplayedId = 20040, ReadOnly = true }];
        }
        var page = new Page { AllPlacements = [first, second] };
        EplanProjects.Project = new(page);
        try
        {
            var reader = new EplanDrawingReader();
            PageDrawingGeometry before = reader.Read("project", page.Name)!;
            Assert.Equal(6, before.Placements.Count);
            first.SwitchLocalPropertyPlacements(true);
            PageDrawingGeometry after = reader.Read("project", page.Name)!;
            Assert.Equal(BridgeJson.FromObject(before).ToString(), BridgeJson.FromObject(after).ToString());
            Assert.False(second.UseLocalPropertyPlacements);
        }
        finally { EplanProjects.Project = null; }
    }

    [Fact]
    public void PropertySelector_ChangesOnlySelectedSlot_WithRadiansAndRelativePosition()
    {
        var owner = new Function(1) { Location = new(20, 20) };
        var first = new PropertyPlacement(1, owner) { Location = new(2, 3) };
        var second = new PropertyPlacement(1, owner) { Location = new(4, 5), IsDocked = true };
        owner.PropertyPlacements = [first, second];
        var page = new Page { AllPlacements = [owner] };
        EplanProjects.Project = new(page);
        try
        {
            JObject row = Row(second, owner, [60, 80]);
            row["propertyId"] = 20214;
            row["propertyIndex"] = 1;
            row["angle"] = 90;
            new EplanPlacementMover().Prepare(new() { Project = new() { Path = "project" }, Page = new() { Page = page.Name } }, new JArray(row))();
            Assert.Equal(new PointD(40, 60), second.Location);
            Assert.Equal(Math.PI / 2, second.Rotation);
            Assert.False(second.IsDocked);
            Assert.Equal(new PointD(2, 3), first.Location);
            Assert.Equal(new PointD(20, 20), owner.Location);
        }
        finally { EplanProjects.Project = null; }
    }

    [Fact]
    public void Move_MixedRootsAndSameIdentifierChildren_UsesRelativeOffsetsAndBothLineEndpoints()
    {
        var symbol = new Function(1) { Location = new(20, 20) };
        var child = new PropertyPlacement(1, symbol) { Location = new(2, 3) };
        var second = new PropertyPlacement(1, symbol) { Location = new(4, 5) };
        symbol.PropertyPlacements = [child, second];
        var text = new Text(2) { Location = new(40, 40) };
        var line = new Line(3) { Location = new(60, 60), StartPoint = new(60, 60), EndPoint = new(70, 80) };
        var rectangle = new Rectangle(4) { Location = new(90, 90) };
        var outside = new Text(5) { Location = new(120, 120) };
        var page = new Page { AllPlacements = [symbol, text, line, rectangle, outside] };
        EplanProjects.Project = new(page);
        try
        {
            var scene = new ConnectionCreatePlanner.Scene { Project = new() { Path = "project" }, Page = new() { Page = page.Name } };
            var rows = new JArray();
            foreach (Placement root in new Placement[] { symbol, text, line, rectangle })
            {
                rows.Add(Row(root, null, [root.Location.X + 10, root.Location.Y + 15]));
                if (root == symbol)
                { rows.Add(Row(child, symbol, [32, 38])); rows.Add(Row(second, symbol, [34, 40])); }
            }
            new EplanPlacementMover().Prepare(scene, rows)();
            Assert.Equal(new PointD(30, 35), symbol.Location);
            Assert.Equal(new PointD(2, 3), child.Location);
            Assert.Equal(new PointD(4, 5), second.Location);
            Assert.Equal(new PointD(50, 55), text.Location);
            Assert.Equal(new PointD(70, 75), line.StartPoint);
            Assert.Equal(new PointD(80, 95), line.EndPoint);
            Assert.Equal(new PointD(100, 105), rectangle.Location);
            Assert.Equal(new PointD(120, 120), outside.Location);
        }
        finally { EplanProjects.Project = null; }
    }

    private static JObject Row(Placement placement, Placement? parent, double[] after) => new()
    {
        ["locator"] = BridgeJson.FromObject(DrawingLocators.FromIdentifier("project", "+A1/1", placement.ToStringIdentifier(), new string('a', 64))),
        ["parent"] = parent is null ? null : BridgeJson.FromObject(DrawingLocators.FromIdentifier("project", "+A1/1", parent.ToStringIdentifier(), new string('b', 64))),
        ["after"] = new JArray(after),
    };
}

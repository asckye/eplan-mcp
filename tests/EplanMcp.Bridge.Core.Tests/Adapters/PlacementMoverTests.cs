using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Project-owned fake placements throw if the adapter disposes them (A39).

[Trait("Size", "Small")]
public sealed class PlacementMoverTests
{
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

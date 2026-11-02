using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // The fake page owns its placements, as EPLAN does (A39).

[Trait("Size", "Small")]
public sealed class ConnectionSceneTests
{
    [Fact]
    public void BlockDisplay_ReadbackIncludesTheDisplayedPropertyId()
    {
        var function = new Function(2265) { Location = new(80, 160) };
        function.PropertyPlacements = [new(1963, function) { DisplayedId = 20201, Location = new(4, 0), Bounds = [new(0, 0), new(0, 0)], Contents = new("??_??@-E103-/3;"), Display = "-E103-/3" }];
        EplanProjects.Project = new(new Page { AllPlacements = [function] });
        try
        {
            var display = Assert.Single(new EplanDrawingReader().Read("project", "+A1/1")!.Placements, p => p.Kind == "PropertyPlacement");
            Assert.Equal("20201", display.Properties["displayed_property_id"]);
            Assert.Equal("??_??@-E103-/3;", display.Properties["text"]);
            Assert.Equal("-E103-/3", display.Properties["display"]);
            Assert.Equal([84d, 160d], display.Location);
            Assert.Equal(2265, display.Parent!.Oid);
        }
        finally { EplanProjects.Project = null; }
    }

    [Fact]
    public void DanglingFixture_DynamicWireWithRepeatedPinIndex_IsAnEdgeWithoutTerminatingPins()
    {
        var wire = new DynamicConnectionLine(1)
        {
            GraphicalConnectionPoints = [new() { Index = 0, Location = new(120, 160) }, new() { Index = 0, Location = new(200, 160) }],
        };
        var scene = new ConnectionCreatePlanner.Scene { Page = new() { GridSize = 4 }, Graphical = [[[120, 160], [200, 160]]] };
        EplanConnectionScene.ReadPins(scene, [wire]);
        Assert.Empty(scene.Pins);
        JArray issues = PageReviewRules.Check(scene, JObject.Parse("""{"connections":[]}"""), new() { ["checks"] = new JArray("dangling_ends") });
        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue => Assert.Equal("dangling_end", (string?)issue["code"]));

        var device = new Function(2) { Location = new(120, 160), GraphicalConnectionPoints = [new() { Index = 0, Direction = "Right" }] };
        EplanConnectionScene.ReadPins(scene, [wire, device, device]);
        Assert.Equal(new double[] { 120, 160 }, Assert.Single(scene.Pins).Value.Location);
        Assert.Single(PageReviewRules.Check(scene, JObject.Parse("""{"connections":[]}"""), new() { ["checks"] = new JArray("dangling_ends") }));
    }
}

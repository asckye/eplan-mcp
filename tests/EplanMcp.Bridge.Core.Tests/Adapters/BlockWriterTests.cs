using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Project-owned fake placements deliberately reject disposal.
[Trait("Size", "Medium")]
public sealed class BlockWriterTests
{
    [Theory]
    [InlineData("144_268557", false)]
    [InlineData(null, false)]
    [InlineData("144_268557", true)]
    public void CreatesBlockWithoutTouchingOutsidePlacement(string? name, bool grouped)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        page.AllPlacements = [new Line(1), new Line(2), new Line(3), new Text(4)];
        if (grouped)
        { foreach (Placement placement in page.AllPlacements) { placement.Group = new Group(900); } }
        var reader = new EplanDrawingReader();
        List<DrawingLocator> members = reader.Read("project", page.Name)!.Placements.Where(p => p.Kind == "Line").Select(p => p.Locator).ToList();
        Placement outside = page.AllPlacements[3];
        var actual = new EplanPlacementTextWriter().Create("project", page.Name, new() { Kind = "block", Members = members, Name = name });
        Assert.Equal(3, actual.Count);
        Assert.Equal(name ?? "", actual.Name);
        Assert.Contains(outside, page.AllPlacements);
        DrawingPlacement row = Assert.Single(reader.Read("project", page.Name)!.Placements, p => p.Kind == "Block");
        Assert.Equal("3", row.Properties["count"]);
        Assert.Equal(name ?? "", row.Properties["name"]);
        Assert.All(reader.Read("project", page.Name)!.Placements.Where(p => p.Kind == "Line"), p => { Assert.NotNull(p.Parent); Assert.Empty(p.Location); Assert.Null(p.Bounds); Assert.Contains("unverified", p.Properties["coordinate_space"], StringComparison.Ordinal); });
        Block native = Assert.Single(page.AllPlacements.OfType<Block>());
        string original = row.Locator.Digest;
        native.Name = "changed";
        Assert.NotEqual(original, Assert.Single(reader.Read("project", page.Name)!.Placements, p => p.Kind == "Block").Locator.Digest);
        page.AllPlacements = [.. native.SubPlacements, .. page.AllPlacements];
        Assert.All(reader.Read("project", page.Name)!.Placements.Where(p => p.Kind == "Line"), p => Assert.NotNull(p.Parent));
    }
}

using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Tests.Adapters;
#pragma warning disable CA2000 // Project-owned native fakes are borrowed.
[Trait("Size", "Small")]
public sealed class PlacementStyleTests
{
    [Fact]
    public void UnreadableStyle_PreservesThePageInventoryWithWarning()
    {
        var line = new Line { PenError = new global::Eplan.EplApi.Base.BaseException("Style unavailable") };
        var page = new Page { AllPlacements = [line] };
        EplanProjects.Project = new(page);
        DrawingPlacement actual = Assert.Single(new EplanDrawingReader().Read("project", page.Name)!.Placements);
        Assert.Equal("Line", actual.Kind);
        Assert.Contains("Style metadata unavailable", actual.Properties["read_warning"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("rectangle")]
    [InlineData("text")]
    [InlineData("path_text")]
    [InlineData("polyline")]
    public void FullStyle_PreservesSentinelsLayerRotationAndClosedVertices(string kind)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        var item = new PlacementText
        {
            Kind = kind,
            Position = [20, 20],
            EndPosition = kind is "line" or "rectangle" ? [40, 40] : [],
            Points = kind == "polyline" ? [[20, 20], [30, 40], [40, 20]] : [],
            HeightMm = 3.5,
            Contents = new() { ["??_??"] = "style" },
            Angle = kind is "text" or "path_text" or "rectangle" ? 90 : 0,
            Layer = 3,
            Alignment = kind is "text" or "path_text" ? "MiddleLeft" : null,
            Closed = kind == "polyline",
            Pen = new() { ["color"] = -16002, ["width_mm"] = -16002, ["style"] = -16002, ["pattern_length"] = -16002, ["line_end"] = 2 }
        };
        var writer = new EplanPlacementTextWriter();
        writer.Validate("project", page.Name, item);
        PlacementText actual = writer.Create("project", page.Name, item);
        Placement native = Assert.Single(page.AllPlacements);
        Assert.Equal(3, native.LayerId);
        Assert.Equal(item.Pen, actual.Pen);
        Assert.Equal(item.Alignment, actual.Alignment);
        Assert.Equal(item.Angle, actual.Angle, 8);
        if (native is TextBase text)
        { Assert.Equal(Math.PI / 2, text.Rotation); }
        if (native is Rectangle rectangle)
        { Assert.Equal(Math.PI / 2, rectangle.Angle); }
        if (native is PolyLine)
        { Assert.True(actual.Closed); Assert.Equal(item.Points.Length, actual.Points.Length); for (int i = 0; i < item.Points.Length; i++) { Assert.Equal(item.Points[i], actual.Points[i]); } }
    }
    [Theory]
    [InlineData(999, null)]
    [InlineData(3, "middleleft")]
    [InlineData(3, "999")]
    public void InvalidLayerOrAlignment_RefusesBeforeCreating(int layer, string? alignment)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new EplanPlacementTextWriter().Validate("project", page.Name,
            new() { Kind = "text", Layer = layer, Alignment = alignment })).Error.Reason);
        Assert.Empty(page.AllPlacements);
    }
    [Theory]
    [InlineData("color")]
    [InlineData("width_mm")]
    [InlineData("style")]
    [InlineData("pattern_length")]
    [InlineData("line_end")]
    [InlineData("layer")]
    [InlineData("alignment")]
    [InlineData("closed")]
    public void StyleReadbackMismatch_RollsBackAllPrimitives(string field)
    {
        var store = new PlacementTextStore { Readback = item => { if (field == "layer") { item.Layer++; } else if (field == "alignment") { item.Alignment = "FromLayer"; } else if (field == "closed") { item.Closed = !item.Closed; } else { item.Pen![field]++; } return item; } };
        var request = PlacementTextStore.Args();
        request.Text.Layer = 3;
        request.Text.Alignment = "MiddleLeft";
        request.Text.Pen = new() { ["color"] = 1, ["width_mm"] = 0.25, ["style"] = 1, ["pattern_length"] = 8, ["line_end"] = 1 };
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(request)).Error.Reason);
        Assert.Empty(store.Texts);
        Assert.Contains("Rollback", store.Transactions.Calls);
    }
}

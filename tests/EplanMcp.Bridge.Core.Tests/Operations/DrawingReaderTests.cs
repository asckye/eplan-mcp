using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Tests.Operations;

#pragma warning disable CA2000 // Fake borrowed EPLAN objects deliberately throw on Dispose to test ownership.

[Trait("Size", "Medium")]
public sealed class DrawingReaderTests
{
    [Fact]
    public void FakeBridge_FramedPage_ExcludesFrameAndUnknownObjectsAndKeepsParent()
    {
        var frameText = new Text(1);
        var function = new Function(2) { Location = new(100, 100) };
        var label = new PropertyPlacement(3, function) { Location = new(4, 6), Bounds = [new(104, 106), new(130, 110)] };
        function.PropertyPlacements = [label];
        var freeText = new Text(4);
        var line = new Line(5) { StartPoint = new(1, 2), EndPoint = new(3, 4) };
        var page = new Page { PlotFrame = new(new Group(6, frameText)), AllPlacements = [label, frameText, function, freeText, line, new Rectangle(7), new Placement(8)] };
        var bridge = new FakeDrawingBridge(page);

        PageDrawingGeometry result = bridge.Read("+A1/1")!;

        Assert.Equal(("+A1/1", 4), (result.Page, result.GridSize));
        Assert.Equal([420, 297], result.PageSize!);
        Assert.Equal([420, 297], result.PlotFrameSize!);
        Assert.Equal(5, result.Placements.Count);
        DrawingPlacement shown = Assert.Single(result.Placements, p => p.Kind == "PropertyPlacement");
        Assert.Equal([104, 106], shown.Location);
        Assert.NotNull(shown.Properties["text"]);
        Assert.NotNull(shown.Properties["display"]);
        Assert.Equal("+A1-K1", Assert.Single(result.Placements, p => p.Kind == "Function").Properties["name"]);
        Assert.Equal([104, 106, 130, 110], shown.Bounds!);
        Assert.Same(Assert.Single(result.Placements, p => p.Kind == "Function").Locator, shown.Parent);
        Assert.True(freeText.Contents.Disposed);
        Assert.True(label.Contents.Disposed);
        Assert.DoesNotContain("53/", JsonConvert.SerializeObject(result), StringComparison.Ordinal);
        Assert.Equal(1, bridge.DispatchCalls);
    }

    [Fact]
    public void FakeBridge_EmptyPageSizeUnavailable_DoesNotUseFrameAsBoundary()
    {
        var page = new Page { SizeError = new BaseException(), PlotFrame = new() };
        PageDrawingGeometry result = new FakeDrawingBridge(page).Read("+A1/1")!;
        Assert.Null(result.PageSize);
        Assert.Equal([420, 297], result.PlotFrameSize!);
        Assert.Empty(result.Placements);
        Assert.False(DrawingCoordinates.Contains(result.PageSize, [[0, 0, 10, 10]]));
    }

    [Fact]
    public void FakeBridge_DefaultPropertyBoundsUnavailable_PreservesUnknown()
    {
        var function = new Function(2);
        function.PropertyPlacements = [new(3, function) { BoundsError = new InvalidOperationException() }];
        PageDrawingGeometry result = new FakeDrawingBridge(new Page { AllPlacements = [function] }).Read("+A1/1")!;
        Assert.Null(Assert.Single(result.Placements, p => p.Kind == "PropertyPlacement").Bounds);
        Assert.False(DrawingCoordinates.Contains(result.PageSize, result.Placements.Select(p => p.Bounds)));
    }

    [Fact]
    public void FakeBridge_SameNumberAfterReopen_IsStableButActualChangesAreStale()
    {
        var line = new Line(5) { StartPoint = new(1, 2), EndPoint = new(3, 4) };
        var text = new Text(4);
        var bridge = new FakeDrawingBridge(new Page { AllPlacements = [line, text] });
        PageDrawingGeometry before = bridge.Read("+A1/1")!;
        line.Prefix = "84";
        text.Prefix = "84";
        PageDrawingGeometry reopened = bridge.Read("+A1/1")!;
        Assert.True(DrawingLocators.Matches(before.Placements[0].Locator, reopened.Placements[0].Locator));
        Assert.True(DrawingLocators.Matches(before.Placements[1].Locator, reopened.Placements[1].Locator));

        // Same insertion point and outer box: content and line endpoints must still invalidate the evidence.
        line.EndPoint = new(4, 4);
        text.Contents = new("en_US@Changed;");
        PageDrawingGeometry changed = bridge.Read("+A1/1")!;
        Assert.False(DrawingLocators.Matches(before.Placements[0].Locator, changed.Placements[0].Locator));
        Assert.False(DrawingLocators.Matches(before.Placements[1].Locator, changed.Placements[1].Locator));
    }

    [Fact]
    public void FakeBridge_PropertyReparentedWithSameGeometry_DigestChanges()
    {
        var firstParent = new Function(2);
        var secondParent = new Function(4);
        firstParent.PropertyPlacements = [new(3, firstParent)];
        var bridge = new FakeDrawingBridge(new Page { AllPlacements = [firstParent, secondParent] });
        DrawingPlacement before = Assert.Single(bridge.Read("+A1/1")!.Placements, p => p.Kind == "PropertyPlacement");
        firstParent.PropertyPlacements = [];
        secondParent.PropertyPlacements = [new(3, secondParent)];
        DrawingPlacement after = Assert.Single(bridge.Read("+A1/1")!.Placements, p => p.Kind == "PropertyPlacement");

        Assert.Equal(before.Location, after.Location);
        Assert.Equal(before.Bounds, after.Bounds);
        Assert.False(DrawingLocators.Matches(before.Locator, after.Locator));
        Assert.NotEqual(before.Parent!.Oid, after.Parent!.Oid);
    }

    [Fact]
    public void FakeBridge_ExactPageAndFiniteGrid_AreRequired()
    {
        var page = new Page();
        var bridge = new FakeDrawingBridge(page);
        Assert.Null(bridge.Read("+a1/1"));
        Assert.Null(bridge.Read("+A1"));
        page.GridSize = double.NaN;
        Assert.Throws<BridgeErrorException>(() => bridge.Read("+A1/1"));
        page.GridSize = 0;
        Assert.Throws<BridgeErrorException>(() => bridge.Read("+A1/1"));
    }

    private sealed class FakeDrawingBridge(params Page[] pages)
    {
        private readonly InlineDispatcher _dispatcher = new();
        public int DispatchCalls => _dispatcher.Calls;
        public PageDrawingGeometry? Read(string page)
        {
            EplanProjects.Project = new(pages);
            var reader = new EplanDrawingReader();
            PageDrawingGeometry? result = null;
            Exception? failure = null;
            _dispatcher.Run(() =>
            {
                try
                { result = reader.Read("project", page); }
                catch (Exception ex) { failure = ex; }
                return null;
            }, TimeSpan.FromSeconds(1));
            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return result;
        }
    }
}

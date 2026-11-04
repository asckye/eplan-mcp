using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Borrowed fake objects throw on Dispose, matching native ownership.

[Trait("Size", "Small")]
public sealed class LayoutMeasurementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableGraphicsMetadata_WarnsAndKeepsMeasuredGeometry(bool invalidOperation)
    {
        var image = new Image { Location = new(40, 40), Bounds = [new(38, 38), new(42, 42)], FileNameError = invalidOperation ? new InvalidOperationException() : new BaseException() };
        var page = new Page { AllPlacements = [image] };
        EplanProjects.Project = new(page);
        try
        {
            DrawingPlacement actual = Assert.Single(new EplanDrawingReader().Read("project", page.Name)!.Placements);
            Assert.Equal("Image", actual.Kind);
            Assert.Equal([38d, 38d, 42d, 42d], actual.Bounds!);
            Assert.Contains("metadata unavailable", actual.Properties["read_warning"], StringComparison.Ordinal);
            Assert.False(actual.Properties.ContainsKey("file"));
        }
        finally { EplanProjects.Project = null; }
    }

    [Theory]
    [InlineData("PLCBox", false)]
    [InlineData("PLCBox", true)]
    [InlineData("BlackBox", true)]
    public void CardMembershipOrReadableParent_IsKeptForSharedOutlines(string category, bool fromParent)
    {
        Function card = fromParent ? new Function(10232) : new PLC();
        card.Location = new(88, 44);
        card.Bounds = [new(68, 36), new(108, 65.25)];
        var a = new Function(69874) { Location = new(88, 44), Bounds = card.Bounds, ParentFunction = fromParent ? card : null };
        var b = new Terminal { Location = new(88, 44), Bounds = card.Bounds, ParentFunction = fromParent ? card : null };
        // A55: do not depend on Terminal.PLC, which can be null for a placed channel.
        if (card is PLC plc)
        { plc.PLCTerminals = [b]; }
        card.FunctionCategory = Enum.Parse<FunctionCategory>(category);
        if (!fromParent)
        { a.ParentFunction = card; }
        var page = new Page { AllPlacements = [card, b] };
        EplanProjects.Project = new(page);
        try
        {
            PageDrawingGeometry geometry = new EplanDrawingReader().Read("project", page.Name, [a])!;
            DrawingPlacement first = Assert.Single(geometry.Placements, p => p.Locator.Oid == a.ObjectIdentifier);
            DrawingPlacement second = Assert.Single(geometry.Placements, p => p.Locator.Oid == b.ObjectIdentifier);
            Assert.Equal(card.ToStringIdentifier(), first.Properties["layout_container_owner"]);
            Assert.Equal(first.Properties["layout_container_owner"], second.Properties["layout_container_owner"]);
            Assert.DoesNotContain(LayoutGuard.Check(new() { Page = geometry }, [first], false), v => v.Rule is "R2" or "R3");
        }
        finally { EplanProjects.Project = null; }
    }

    [Fact]
    public void UnreadableLocation_IsUnknownAndDoesNotBlockMeasuredWrite()
    {
        var placement = new Function(69863) { LocationError = new BaseException(), BoundsError = new BaseException() };
        var page = new Page { AllPlacements = [placement] };
        EplanProjects.Project = new(page);
        try
        {
            PageDrawingGeometry actual = new EplanDrawingReader().Read("project", page.Name)!;
            Assert.Empty(Assert.Single(actual.Placements).Location);
            Assert.Contains(LayoutGuard.AfterWriting(new() { Page = new() { PageSize = [420, 297], GridSize = 4 } }, new() { Page = actual }),
                s => s.Contains("unknown positions", StringComparison.Ordinal));
        }
        finally { EplanProjects.Project = null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnedUncommittedFunctionAndSameOidText_AreMeasuredEvenWhenPageEnumerationCannotSeeThem(bool overlap)
    {
        var page = new Page();
        var function = new Function(2) { Location = new(40, 40), Bounds = [new(38, 38), new(42, 42)] };
        var label = new PropertyPlacement(2, function)
        {
            Location = new(8, 0),
            Bounds = overlap ? [new(40, 40), new(52, 44)] : [new(48, 40), new(60, 44)]
        };
        function.PropertyPlacements = [label];
        EplanProjects.Project = new(page);
        try
        {
            var reader = new EplanDrawingReader();
            PageDrawingGeometry before = reader.Read("project", page.Name)!;
            Assert.Empty(before.Placements);
            PageDrawingGeometry actual = reader.Read("project", page.Name, [function])!;
            Assert.Equal(2, actual.Placements.Count);
            DrawingPlacement text = Assert.Single(actual.Placements, p => p.Kind == "PropertyPlacement");
            Assert.Equal(function.ObjectIdentifier, text.Locator.Oid);
            Assert.Equal([48d, 40d], text.Location);
            Assert.NotEqual(text.Locator.Digest, text.Parent!.Digest);
            if (overlap)
            {
                List<string> warnings = LayoutGuard.AfterWriting(new() { Page = before }, new() { Page = actual });
                Assert.Contains(warnings, s => s.StartsWith("R3: Warning:", StringComparison.Ordinal));
            }
            else
            { Assert.Contains(LayoutGuard.AfterWriting(new() { Page = before }, new() { Page = actual }), s => s.Contains("after write", StringComparison.Ordinal)); }
            Assert.Empty(page.AllPlacements);
        }
        finally { EplanProjects.Project = null; }
    }

    [Fact]
    public void UnreadablePlotFrame_UsesPageSizeMinusMarginAndReportsFallback()
    {
        var page = new Page { FrameError = new BaseException() };
        EplanProjects.Project = new(page);
        try
        {
            PageDrawingGeometry actual = new EplanDrawingReader().Read("project", page.Name)!;
            var model = new LayoutGuard.Model { Page = actual };
            Assert.Null(actual.PlotFrameSize);
            Assert.Equal([10d, 10d, 410d, 287d], LayoutGuard.Area(model));
            Assert.Contains(LayoutGuard.Enforce(model, [LayoutGuard.Proposed("Function", [40, 40], null)]), s => s.Contains("Boundary fallback", StringComparison.Ordinal));
        }
        finally { EplanProjects.Project = null; }
    }
}

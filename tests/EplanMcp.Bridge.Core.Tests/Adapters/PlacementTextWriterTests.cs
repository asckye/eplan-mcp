using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Native fakes deliberately reject disposal of project-owned objects (A39/A55).

[Trait("Size", "Small")]
public sealed class PlacementTextWriterTests
{
    [Fact]
    public void Mixed_ReadsEarlierTextOnlyAfterAllPlacementsHaveBeenCreated()
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        page.AfterGraphicCreate = () => Assert.IsType<Text>(page.AllPlacements[0]).Height = 9;
        PlacementTextCreateArgs args = PlacementTextStore.Mixed();
        List<PlacementText> actual = new EplanPlacementTextWriter().CreateBatch("project", "+A1/1", args.Items!);
        Assert.Equal(3, page.AllPlacements.Length);
        Assert.Equal(9, actual[0].HeightMm);
        Assert.Equal(3.5, args.Items![0].HeightMm);
    }
    [Theory]
    [InlineData("line")]
    [InlineData("rectangle")]
    public void Graphics_CreateAndReadBackBothPointsWithoutTextLanguageRequirement(string kind)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        var requested = new PlacementText { Kind = kind, Position = [12.5, 22.5], EndPosition = [35, 48] };
        var writer = new EplanPlacementTextWriter();
        writer.Validate("project", "+A1/1", requested);
        PlacementText actual = writer.Create("project", "+A1/1", requested);
        Assert.Equal(kind, actual.Kind);
        Assert.Equal(requested.Position, actual.Position);
        Assert.Equal(requested.EndPosition, actual.EndPosition);
        Assert.Empty(actual.Contents);
        Assert.IsType(kind == "line" ? typeof(Line) : typeof(Rectangle), Assert.Single(page.AllPlacements));
    }
    [Theory]
    [InlineData("zh_CN", true)]
    [InlineData("??_??", true)]
    [InlineData("en_US", false)]
    [InlineData("xx_YY", false)]
    public void Validate_UsesActualProjectDisplayLanguageAndRejectsUnknownIso(string language, bool valid)
    {
        EplanProjects.Project = new(new Page());
        var text = new PlacementText { Position = [12.5, 22.5], HeightMm = 3.5, Contents = new() { [language] = "Panel" } };
        var writer = new EplanPlacementTextWriter();
        if (valid)
        { writer.Validate("project", "+A1/1", text); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => writer.Validate("project", "+A1/1", text)).Error.Reason); }
        Assert.Empty(EplanProjects.Project.Pages[0].AllPlacements);
    }

    [Theory]
    [InlineData("+a1/1", ErrorReason.PageNotFound)]
    [InlineData("+A1", ErrorReason.PageNotFound)]
    [InlineData("+A1/1", ErrorReason.InvalidArgument)]
    public void Validate_RequiresExactAllowedPage(string name, string reason)
    {
        EplanProjects.Project = new(new Page { PageType = DocumentTypeManager.DocumentType.TableOfContents });
        Assert.Equal(reason, Assert.Throws<BridgeErrorException>(() => new EplanPlacementTextWriter().Validate("project", name, PlacementTextStore.Args().Text)).Error.Reason);
    }

    [Theory]
    [InlineData("Circuit")]
    [InlineData("Overview")]
    [InlineData("Graphics")]
    public void Validate_AllowsDrawingPageTypes(string type)
    {
        EplanProjects.Project = new(new Page { PageType = Enum.Parse<DocumentTypeManager.DocumentType>(type) });
        var text = PlacementTextStore.Args().Text;
        text.Contents = new() { ["??_??"] = "Panel" };
        new EplanPlacementTextWriter().Validate("project", "+A1/1", text);
    }

    [Theory]
    [InlineData(0, 360, "Circle")]
    [InlineData(-90, 270, "Circle")]
    [InlineData(90, 90, "Circle")]
    [InlineData(30, 120, "Arc")]
    public void Arc_SelectsCircleOnlyForEqualAnglesModulo360(double start, double end, string expected)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        var requested = new PlacementText { Kind = "arc", Position = [50, 50], RadiusMm = 8, StartAngle = start, EndAngle = end };
        PlacementText actual = new EplanPlacementTextWriter().Create("project", "+A1/1", requested);
        Arc native = Assert.IsType<Arc>(Assert.Single(page.AllPlacements));
        Assert.Equal(expected, native.Kind);
        Assert.Equal(8, actual.RadiusMm);
        if (expected == "Arc")
        { Assert.Equal(start, actual.StartAngle, 9); Assert.Equal(end, actual.EndAngle, 9); }
    }

    [Fact]
    public void Ellipse_UsesRadiansAndEllipseSpecificReadback()
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        var requested = new PlacementText { Kind = "ellipse", Position = [50, 50], RadiusXmm = 1.43748, RadiusYmm = 1.43748, Angle = 90 };
        PlacementText actual = new EplanPlacementTextWriter().Create("project", "+A1/1", requested);
        Arc native = Assert.IsType<Arc>(Assert.Single(page.AllPlacements));
        Assert.Equal("Ellipse", native.Kind);
        Assert.Equal(Math.PI / 2, native.ElipseAngle, 9);
        Assert.Equal((0d, 0d, 0d), (native.Radius, native.StartAngle, native.EndAngle));
        Assert.Equal((requested.RadiusXmm, requested.RadiusYmm, requested.Angle), (actual.RadiusXmm, actual.RadiusYmm, actual.Angle));
        DrawingPlacement row = Assert.Single(new EplanDrawingReader().Read("project", "+A1/1")!.Placements);
        Assert.Equal("Ellipse", row.Properties["arc_kind"]);
        Assert.Equal("1.43748", row.Properties["radius_x_mm"]);
    }

    [Theory]
    [InlineData("ellipse")]
    [InlineData("arc")]
    public void CircleAndEllipse_RejectWrongNativeKind(string kind)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        page.AfterGraphicCreate = () => Assert.IsType<Arc>(page.AllPlacements[0]).Kind = "Arc";
        var requested = new PlacementText { Kind = kind, Position = [50, 50], RadiusMm = kind == "arc" ? 8 : 0, EndAngle = 360, RadiusXmm = 4, RadiusYmm = 8 };
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => new EplanPlacementTextWriter().CreateBatch("project", "+A1/1",
            [requested, new() { Kind = "line", Position = [20, 20], EndPosition = [30, 20] }])).Error.Reason);
    }

    [Fact]
    public void PathText_DefaultHeightIsPassedToCreateAndReadBackUnchanged()
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        var requested = new PlacementText { Kind = "path_text", Position = [20, 20], HeightMm = -16002, Contents = new() { ["??_??"] = "Path text" } };
        PlacementText actual = new EplanPlacementTextWriter().Create("project", "+A1/1", requested);
        Assert.Equal(-16002, Assert.IsType<PathText>(Assert.Single(page.AllPlacements)).Height);
        Assert.Equal(-16002, actual.HeightMm);
        Assert.Equal("-16002", Assert.Single(new EplanDrawingReader().Read("project", "+A1/1")!.Placements).Properties["height_mm"]);
    }

    [Fact]
    public void Create_RequestedLanguagesHeightAndPosition_AreDetachedAndReadBack()
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        PlacementText requested = PlacementTextStore.Args().Text;
        requested.Contents["zh_CN"] = "Visible panel";
        var writer = new EplanPlacementTextWriter();
        writer.Validate("project", "+A1/1", requested);
        PlacementText actual = writer.Create("project", "+A1/1", requested);
        Text native = Assert.IsType<Text>(Assert.Single(page.AllPlacements));
        Assert.Equal(requested.Contents, actual.Contents);
        Assert.Equal(requested.HeightMm, actual.HeightMm);
        Assert.Equal(requested.Position, actual.Position);
        Assert.Equal((12.5, 22.5), (native.Location.X, native.Location.Y));
        Assert.True(native.Contents.Disposed);
        native.Height = 9;
        native.Location = new(1, 2);
        native.Contents.AddString(ISOCode.Language.en_US, "Later");
        Assert.Equal(3.5, actual.HeightMm);
        Assert.Equal([12.5, 22.5], actual.Position);
        Assert.Equal("Panel", actual.Contents["en_US"]);
    }
}

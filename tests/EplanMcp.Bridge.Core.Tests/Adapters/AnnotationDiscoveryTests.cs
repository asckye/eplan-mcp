using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Native placements are borrowed; ownership remains with the page.
[Trait("Size", "Medium")]
public sealed class AnnotationDiscoveryTests
{
    [Fact]
    public void AnnotationsAreDiscoverableWithPositionFieldsAndCanBeDeletedAfterReadOnlyPreparation()
    {
        var page = new Page();
        var hyperlink = new HyperLink { Page = page, Location = new(20, 24), Link = "https://example.test", Contents = new("en_US@Details;") };
        var code = new QRCode { Page = page, Location = new(40, 24), Text = "QR payload" };
        var spline = new Spline { Page = page, Location = new(60, 24) };
        PointD first = new(60, 24), second = new(80, 36);
        spline.SetPointAt(0, ref first);
        spline.SetPointAt(1, ref second);
        var sector = new Arc { Page = page };
        sector.SetArc(new(100, 40), 8, 0, Math.PI / 2, true);
        var dimension = new DimensionItem { Page = page, Location = new(140, 24) };
        var circle = new DimensionCircle { Page = page, Location = new(160, 24) };
        var holder = new PlaceHolder { Page = page, Location = new(180, 24), Name = "Motor" };
        var macro = new MacroBox { Page = page, Location = new(200, 24), MacroName = "Motor.ema" };
        page.AllPlacements = [hyperlink, code, spline, sector, dimension, circle, holder, macro];
        EplanProjects.Project = new(page);

        PageDrawingGeometry found = new EplanDrawingReader().Read("project", page.Name)!;

        Assert.Equal(8, found.Placements.Count);
        Assert.Equal("https://example.test", Row("HyperLink").Properties["link"]);
        Assert.Equal("en_US@Details;", Row("HyperLink").Properties["text"]);
        Assert.Equal("QR payload", Row("QRCode").Properties["text"]);
        Assert.Equal([60d, 24d, 80d, 36d], Newtonsoft.Json.Linq.JArray.Parse(Row("Spline").Properties["points"]).SelectMany(p => p.Select(v => (double)v)).ToArray());
        Assert.Equal("true", Row("Arc").Properties["sector"]);
        // Live 2026-11-04: reading DimensionText after the commit makes EPLAN's undo fail.
        Assert.False(Row("DimensionItem").Properties.ContainsKey("dimension_text"));
        Assert.False(Row("DimensionCircle").Properties.ContainsKey("dimension_text"));
        Assert.Equal("Motor", Row("PlaceHolder").Properties["name"]);
        Assert.Equal("Motor.ema", Row("MacroBox").Properties["macro_name"]);
        Assert.All(found.Placements, row => { Assert.Equal(64, row.Locator.Digest.Length); Assert.Equal(2, row.Location.Length); });
        Action remove = new EplanPlacementRemover().Prepare("project", page.Name, found.Placements.Select(p => p.Locator).ToArray());
        Assert.Equal(8, page.AllPlacements.Length);
        remove();
        Assert.Empty(new EplanDrawingReader().Read("project", page.Name)!.Placements);

        DrawingPlacement Row(string kind) => Assert.Single(found.Placements, p => p.Kind == kind);
    }

    [Fact]
    public void UnreadableAnnotationMetadataWarnsAndRetainsLocatorAndOtherObjects()
    {
        var page = new Page { AllPlacements = [new QRCode { TextError = new BaseException("unreadable") }, new HyperLink { Link = "readable" }] };
        EplanProjects.Project = new(page);
        PageDrawingGeometry found = new EplanDrawingReader().Read("project", page.Name)!;
        Assert.Equal(2, found.Placements.Count);
        Assert.Contains("unavailable", Assert.Single(found.Placements, p => p.Kind == "QRCode").Properties["read_warning"], StringComparison.Ordinal);
        Assert.Equal("readable", Assert.Single(found.Placements, p => p.Kind == "HyperLink").Properties["link"]);
    }

    [Fact]
    public void ChangingAnnotationContentInvalidatesTheReadLocator()
    {
        var code = new QRCode { Text = "before" };
        var page = new Page { AllPlacements = [code] };
        EplanProjects.Project = new(page);
        string before = Assert.Single(new EplanDrawingReader().Read("project", page.Name)!.Placements).Locator.Digest;
        code.Text = "after";
        Assert.NotEqual(before, Assert.Single(new EplanDrawingReader().Read("project", page.Name)!.Placements).Locator.Digest);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("size")]
    [InlineData("invalid_size")]
    public void MissingGeometryDoesNotHideAnnotationObjects(string missing)
    {
        var page = new Page { AllPlacements = [new QRCode { Text = "visible" }] };
        if (missing == "grid")
        { page.GridError = new BaseException("unreadable grid"); }
        else if (missing == "size")
        { page.SizeError = new BaseException("unreadable size"); }
        else
        { page.StoredSize = new(double.NaN, -1); }
        EplanProjects.Project = new(page);
        PageDrawingGeometry found = new EplanDrawingReader().Read("project", page.Name)!;
        Assert.Equal("visible", Assert.Single(found.Placements).Properties["text"]);
        if (missing == "grid")
        { Assert.Equal(0, found.GridSize); }
        else
        { Assert.Null(found.PageSize); }
    }
}
#pragma warning restore CA2000

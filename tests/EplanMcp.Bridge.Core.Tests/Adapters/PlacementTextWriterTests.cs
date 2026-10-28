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
    public void Validate_RequiresExactCircuitPage(string name, string reason)
    {
        EplanProjects.Project = new(new Page { PageType = DocumentTypeManager.DocumentType.Graphics });
        Assert.Equal(reason, Assert.Throws<BridgeErrorException>(() => new EplanPlacementTextWriter().Validate("project", name, PlacementTextStore.Args().Text)).Error.Reason);
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

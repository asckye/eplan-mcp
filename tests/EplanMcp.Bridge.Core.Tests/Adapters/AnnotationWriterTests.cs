using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Bridge.Core.Tests.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Native fakes represent page-owned placements, never disposed by the adapter.
[Trait("Size", "Medium")]
public sealed class AnnotationWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownDisplayLanguageWarnsWithoutRefusingHyperlinks(bool unreadable)
    {
        var page = new Page();
        EplanProjects.Project = new(page) { Display = "", DisplayError = unreadable ? new BridgeErrorException(ErrorReason.EplanOperationFailed, "Unavailable") : null };
        PlacementText item = AnnotationCreateTests.Item("hyperlink");
        item.Contents = new() { ["en_US"] = "Details" };
        Assert.Contains("unreadable", new EplanPlacementTextWriter().Validate("project", page.Name, item), StringComparison.Ordinal);
        Assert.Empty(page.AllPlacements);
    }

    [Theory]
    [MemberData(nameof(AnnotationCreateTests.Kinds), MemberType = typeof(AnnotationCreateTests))]
    public void EachKindCreatesAndReadsNativeContentAndStyle(string kind)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        PlacementText requested = AnnotationCreateTests.Item(kind);
        if (kind is "hyperlink" or "qr_code" or "spline" or "sector")
        {
            requested.Layer = 3;
            requested.Pen = new() { ["color"] = 5, ["width_mm"] = 0.25, ["line_end"] = 1 };
        }
        if (kind == "circle")
        { requested.Arc = AddCircle(page); }
        var writer = new EplanPlacementTextWriter();
        writer.Validate("project", page.Name, requested);
        PlacementText actual = writer.Create("project", page.Name, requested);

        Assert.Equal(requested.Kind, actual.Kind);
        Assert.Equal(requested.DimensionType, actual.DimensionType);
        Assert.Equal(requested.Layer, actual.Layer);
        Assert.Equal(requested.Pen, actual.Pen);
        switch (kind)
        {
            case "hyperlink":
                Assert.Equal(requested.Link, actual.Link);
                Assert.Equal(requested.Contents, actual.Contents);
                break;
            case "qr_code":
                Assert.Equal((requested.Text, requested.SizeMm), (actual.Text, actual.SizeMm));
                break;
            case "spline":
                Assert.Equal(JsonConvert.SerializeObject(requested.Points), JsonConvert.SerializeObject(actual.Points));
                break;
            case "sector":
                Assert.True(Assert.IsType<Arc>(Assert.Single(page.AllPlacements)).Sector);
                Assert.Equal(requested.RadiusMm, actual.RadiusMm);
                break;
            case "placeholder":
                Assert.Equal(requested.Name, actual.Name);
                Assert.Equal(requested.Records, actual.Records);
                Assert.Equal(requested.Variables, actual.Variables);
                break;
            case "macro_box":
                Assert.Equal(requested.Name, actual.Name);
                break;
            default:
                Assert.Equal(kind is "baseline" or "incremental" or "continued" ? 2 : 1, actual.DimensionText!.Length);
                if (kind is "linear" or "angular")
                { Assert.Equal(JsonConvert.SerializeObject(requested.Points), JsonConvert.SerializeObject(actual.Points)); Assert.Equal(requested.EndPosition, actual.EndPosition); }
                break;
        }
    }

    [Theory]
    [InlineData(0, 360)]
    [InlineData(90, 90)]
    [InlineData(-90, 270)]
    public void SectorAlwaysSetsSectorFlagIncludingCoincidentAngles(double start, double end)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        PlacementText request = AnnotationCreateTests.Item("sector");
        request.StartAngle = start;
        request.EndAngle = end;
        PlacementText actual = new EplanPlacementTextWriter().Create("project", page.Name, request);
        Arc native = Assert.IsType<Arc>(Assert.Single(page.AllPlacements));
        Assert.True(native.Sector);
        Assert.Equal(start * Math.PI / 180, native.StartAngle, 9);
        Assert.Equal(end * Math.PI / 180, native.EndAngle, 9);
        Assert.Equal(start, actual.StartAngle, 9);
        Assert.Equal(end, actual.EndAngle, 9);
    }




    private static DrawingLocator AddCircle(Page page)
    {
        var arc = new Arc { Page = page };
        arc.Create(page);
        arc.SetCircle(new(320, 30), 6);
        return new()
        {
            Project = "project",
            Page = page.Name,
            Type = arc.IdentifierType,
            Oid = arc.ObjectIdentifier,
            Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(EplanObjectReader.Describe(arc, "arc")))
        };
    }
}
#pragma warning restore CA2000

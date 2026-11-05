using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Tests.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
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

    [Theory]
    [InlineData("kind", ErrorReason.InvalidArgument)]
    [InlineData("missing", ErrorReason.PlanStale)]
    [InlineData("digest", ErrorReason.PlanStale)]
    [InlineData("page", ErrorReason.InvalidArgument)]
    public void CircleRequiresFreshWholeCircleOnTargetPageBeforeAnyCreate(string changed, string reason)
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        PlacementText request = AnnotationCreateTests.Item("circle");
        request.Arc = AddCircle(page);
        switch (changed)
        {
            case "kind":
                Assert.IsType<Arc>(page.AllPlacements[0]).Kind = "Arc";
                break;
            case "missing":
                page.AllPlacements = [];
                break;
            case "digest":
                request.Arc.Digest = new('b', 64);
                break;
            case "page":
                request.Arc.Page = "different";
                break;
        }
        int before = page.AllPlacements.Length;
        Assert.Equal(reason, Assert.Throws<BridgeErrorException>(() => new EplanPlacementTextWriter().Validate("project", page.Name, request)).Error.Reason);
        Assert.Equal(before, page.AllPlacements.Length);
    }

    [Fact]
    public void DimensionGroupReadsEveryGeneratedChildsCalculatedText()
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        PlacementText request = AnnotationCreateTests.Item("baseline");
        PlacementText actual = new EplanPlacementTextWriter().Create("project", page.Name, request);
        DimensionItem[] children = page.AllPlacements.OfType<DimensionItem>().ToArray();
        Assert.Equal(2, children.Length);
        Assert.Equal(children.Select(child => child.DimensionText), actual.DimensionText!);
    }

    [Fact]
    public void CircleIsResolvedAgainAfterPreviewBeforeNativeCreation()
    {
        var page = new Page();
        EplanProjects.Project = new(page);
        PlacementText request = AnnotationCreateTests.Item("circle");
        request.Arc = AddCircle(page);
        var writer = new EplanPlacementTextWriter();
        writer.Validate("project", page.Name, request);
        page.AllPlacements = [new Arc { Page = page, Kind = "Arc" }];
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => writer.Create("project", page.Name, request)).Error.Reason);
        Assert.Single(page.AllPlacements);
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

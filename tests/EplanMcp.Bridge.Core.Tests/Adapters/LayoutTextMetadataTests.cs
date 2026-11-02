using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Fake placements are borrowed, matching native ownership.

[Trait("Size", "Small")]
public sealed class LayoutTextMetadataTests
{
    [Theory]
    [InlineData(-16002, "from_layer")]
    [InlineData(-Math.PI / 2, "270")]
    [InlineData(1.5707963267948968, "90")]
    public void LoggedSetupText_RotationReadbackDoesNotRefuseR8(double rotation, string expected)
    {
        var text = new Text(423455) { Location = new(140, 100), Bounds = [new(140, 100), new(184.71, 105.99025)], Height = 3.5, Rotation = rotation };
        PageDrawingGeometry page = Read(text);
        DrawingPlacement row = Assert.Single(page.Placements);
        Assert.Equal(expected, row.Properties["angle"]);
        Assert.Equal("3.5", row.Properties["height_mm"]);
        var before = new LayoutGuard.Model { Page = new() { GridSize = 4, PageSize = [420, 297] }, SetsTextAttribute = (_, _) => true };
        List<string> checks = LayoutGuard.AfterWriting(before, new() { Page = page });
        if (rotation == -16002)
        { Assert.Contains(checks, s => s.StartsWith("R8 unverified", StringComparison.Ordinal)); }
        Assert.DoesNotContain(checks, s => s.StartsWith("R8:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-16002)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InheritedOrUnavailableHeight_IsUnknownEvenWithExplicitFormatting(double height)
    {
        DrawingPlacement row = Assert.Single(Read(new Text(423455) { Location = new(140, 100), Bounds = [new(140, 100), new(184.71, 105.99025)], Height = height }).Placements);
        Assert.Equal("from_layer", row.Properties["height_mm"]);
        var model = new LayoutGuard.Model { Page = new() { PageSize = [420, 297], GridSize = 4 }, SetsTextAttribute = (_, _) => true };
        Assert.DoesNotContain(LayoutGuard.Check(model, [row], false), v => v.Rule == "R8");
        Assert.Contains(LayoutGuard.Enforce(model, [row]), s => s.StartsWith("R8 unverified", StringComparison.Ordinal));
    }

    [Fact]
    public void PropertyAngleAndHeight_ChangeDiscoveryDigest()
    {
        var owner = new Function(69888) { Location = new(80, 208), Bounds = [new(80, 208), new(88, 216)] };
        var text = new PropertyPlacement(69888, owner) { Location = new(12, 0), Bounds = [new(92, 207.47125), new(97.64, 211.75)], DisplayedId = 20214 };
        owner.PropertyPlacements = [text];
        DrawingPlacement Before() => Assert.Single(Read(owner).Placements, p => p.Kind == "PropertyPlacement");
        DrawingPlacement initial = Before();
        text.Rotation = Math.PI / 2;
        DrawingPlacement rotated = Before();
        Assert.Equal("90", rotated.Properties["angle"]);
        Assert.Equal("20214", rotated.Properties["property_id"]);
        Assert.NotEqual(initial.Locator.Digest, rotated.Locator.Digest);
        text.Height = -16002;
        DrawingPlacement inherited = Before();
        Assert.Equal("from_layer", inherited.Properties["height_mm"]);
        Assert.NotEqual(rotated.Locator.Digest, inherited.Locator.Digest);
    }

    private static PageDrawingGeometry Read(Placement placement)
    {
        var page = new Page { AllPlacements = [placement] };
        EplanProjects.Project = new(page);
        try
        { return new EplanDrawingReader().Read("project", page.Name)!; }
        finally { EplanProjects.Project = null; }
    }
}

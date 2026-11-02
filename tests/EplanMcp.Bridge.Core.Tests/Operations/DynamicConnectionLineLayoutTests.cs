using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DynamicConnectionLineLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveDanglingBat_PinTouchesWireEnd_OverlappingWireBoxDoesNotRefuseR2(bool staleSymbolHint)
    {
        var scene = Scene();
        DrawingPlacement wire = scene.Page.Placements[0];
        if (staleSymbolHint)
        { wire.Properties["layout_symbol"] = "true"; }
        DrawingPlacement bat = Bat();
        LayoutGuard.Model model = LayoutGuard.From(scene);
        Assert.True(LayoutGuard.Overlaps(bat.Bounds!, wire.Bounds!));
        Assert.Equal(scene.Pins["17/69839/0"].Location, Assert.Single(model.Lines).Points[0]);
        Assert.DoesNotContain(LayoutGuard.Check(model, [bat], false), v => !v.Warning);
        // R2 must also ignore the wire when it is the changed placement.
        model.Page.Placements = [bat];
        Assert.DoesNotContain(LayoutGuard.Check(model, [wire], false), v => v.Rule == "R2");
    }

    [Fact]
    public void LiveDanglingBat_RealSymbolReferenceWithSameOverlappingBox_StillRefusesR2()
    {
        var scene = Scene();
        DrawingPlacement symbol = scene.Page.Placements[0];
        symbol.Kind = "SymbolReference";
        symbol.Locator.Type = 13;
        Assert.Contains(LayoutGuard.Check(LayoutGuard.From(scene), [Bat()], false), v => v.Rule == "R2" && !v.Warning);
    }

    [Theory]
    [InlineData("R3")]
    [InlineData("R4")]
    public void DynamicWire_ExistingGraphicalLineStillChecksTextAndSymbolCrossings(string rule)
    {
        var scene = Scene();
        DrawingPlacement crossing = new()
        {
            Kind = rule == "R3" ? "Text" : "Function",
            Locator = new() { Type = 17, Oid = 69840 },
            Location = [160, 160],
            Bounds = [156, 156, 164, 164]
        };
        LayoutGuard.Model model = LayoutGuard.From(scene);
        model.SetsTextAttribute = (_, attribute) => attribute == "position";
        Assert.Contains(LayoutGuard.Check(model, [crossing], false), v => v.Rule == rule && !v.Warning);
    }

    [Theory]
    [InlineData("DynamicConnectionLine", 33, 0)]
    [InlineData("SymbolReference", 13, 2)]
    public void PassThroughPins_OnlyBareSymbolReferenceIsANode(string kind, int type, int expectedPins)
    {
        DrawingPlacement placement = Scene().Page.Placements[0];
        placement.Kind = kind;
        placement.Locator.Type = type;
        Dictionary<string, SymbolConnectionPoint> pins = new()
        {
            [FormattableString.Invariant($"{type}/423455/0")] = new() { Location = [120, 160], Direction = "Left" },
            [FormattableString.Invariant($"{type}/423455/1")] = new() { Location = [200, 160], Direction = "Right" }
        };
        Assert.Equal(expectedPins, ConnectionNodeGeometry.PassThroughPins(placement, pins).Length);
    }

    private static DrawingPlacement Bat() => new()
    {
        Kind = "Function",
        Locator = new() { Project = "LargeWrite", Page = "=GAA+A1&EFS1/103", Type = 17, Oid = 69839 },
        Location = [116, 160],
        // A75: the live BAT's measured outline; keep the original helper wire endpoints.
        Bounds = [111.75, 155.75, 120.25, 164.25]
    };

    private static ConnectionCreatePlanner.Scene Scene() => new()
    {
        Page = new()
        {
            Page = "=GAA+A1&EFS1/103",
            Project = "LargeWrite",
            GridSize = 4,
            PageSize = [420, 297],
            Placements = [new()
            {
                Kind = "DynamicConnectionLine",
                Locator = new() { Project = "LargeWrite", Page = "=GAA+A1&EFS1/103", Type = 33, Oid = 423455 },
                Location = [120, 160], Bounds = [120, 160, 200, 160]
            }]
        },
        Graphical = [[[120, 160], [200, 160]]],
        Pins = new() { ["17/69839/0"] = new() { Location = [120, 160], Direction = "Right" } }
    };
}

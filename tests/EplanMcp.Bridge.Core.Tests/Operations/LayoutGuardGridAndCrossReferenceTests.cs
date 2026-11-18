using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using static EplanMcp.Bridge.Core.Tests.Operations.LayoutGuardTests;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardGridAndCrossReferenceTests
{
    private static void CheckRule(LayoutGuard.Model model, DrawingPlacement[] changed, string rule, bool reported, params DrawingLocator[] objects)
    {
        var violations = LayoutGuard.Check(model, changed, suggest: false);
        if (!reported)
        {
            Assert.DoesNotContain(violations, v => v.Rule == rule);
            return;
        }
        var violation = Assert.Single(violations, v => v.Rule == rule);
        Assert.Equal(objects, violation.Objects);
        Assert.Equal(!model.Config.Strict, violation.Warning);
    }

    [Theory]
    [InlineData(3.9999999999999994e-8, false)]
    [InlineData(4e-8, true)]
    [InlineData(4.000000000000001e-8, true)]
    [InlineData(-4e-8, true)]
    [InlineData(39.99999999, false)]
    [InlineData(40.00000001, false)]
    public void R7_PlacementAtGridTolerance_UsesStrictCutoff(double x, bool reported)
    {
        var model = Model();
        model.Config.MarginMm = 0;
        var p = Row("Function", 1, x, 40);
        CheckRule(model, [p], "R7", reported, p.Locator);
    }

    [Theory]
    [InlineData(3.9999999999999994e-8, false)]
    [InlineData(4e-8, true)]
    [InlineData(4.000000000000001e-8, true)]
    public void R7_InteriorBendAtGridTolerance_UsesStrictCutoff(double y, bool reported)
    {
        var model = Model();
        model.Config.MarginMm = 0;
        var line = new LayoutGuard.Segment { Locator = new() { Oid = 7 }, Changed = true, Points = [[20, 0], [24, y], [24, 40]] };
        model.Lines.Add(line);
        CheckRule(model, [], "R7", reported, line.Locator);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R7_OffGridPlacementWithUnknownBounds_NamesPlacementWithConfiguredSeverity(bool strict)
    {
        var model = Model();
        model.Config.Strict = strict;
        var p = Row("Function", 1, 40, 41);
        p.Bounds = null;
        model.Pins["17/1/0"] = new() { Location = p.Location };
        CheckRule(model, [p], "R7", true, p.Locator);
    }

    [Fact]
    public void R7_OffGridChildPlacement_IsExempt()
    {
        var p = Row("PropertyPlacement", 1, 41);
        p.Parent = new() { Oid = 2 };
        CheckRule(Model(), [p], "R7", false);
    }

    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new double[] { double.NaN, 41 })]
    public void R7_UnreadablePlacementLocation_IsSkipped(double[] location)
    {
        var p = Row("Function", 1);
        p.Location = location;
        CheckRule(Model(), [p], "R7", false);
    }

    [Fact]
    public void R7_AllZeroPlacementBounds_IsSkipped()
    {
        var p = Row("Function", 1, 41);
        p.Bounds = [0, 0, 0, 0];
        CheckRule(Model(), [p], "R7", false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R7_OffGridEndpointWithoutMatchingPin_NamesLineWithConfiguredSeverity(bool strict)
    {
        var model = Model();
        model.Config.Strict = strict;
        var line = new LayoutGuard.Segment { Changed = true, Locator = new() { Oid = 7 }, Points = [[41, 40], [60, 40]] };
        model.Lines.Add(line);
        model.Pins["17/1/0"] = new() { Location = [Math.BitIncrement(41), 40] };
        CheckRule(model, [], "R7", true, line.Locator);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void R7_LinePointExactlyMatchingPin_IsExemptEvenAtInteriorBend(int index)
    {
        var model = Model();
        double[][] points = [[20, 40], [40, 40], [40, 60]];
        points[index][0] += 1;
        model.Pins["17/1/0"] = new() { Location = [.. points[index]] };
        model.Lines.Add(new() { Points = points, Changed = true });
        CheckRule(model, [], "R7", false);
    }

    [Fact]
    public void R7_UnchangedOffGridLine_IsExempt()
    {
        var model = Model();
        model.Lines.Add(new() { Points = [[41, 40], [60, 40]] });
        CheckRule(model, [], "R7", false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-double.Epsilon)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void R7_UnknownGridAllowed_SkipsPlacementsAndLines(double grid)
    {
        var model = Model();
        model.AllowUnknownGeometry = true;
        model.Page.GridSize = grid;
        model.Lines.Add(new() { Points = [[41, 40], [60, 40]], Changed = true });
        Assert.Empty(LayoutGuard.Check(model, [Row("Function", 1, 41, 64)], suggest: false));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(double.Epsilon)]
    public void R7_PositiveGridWithUnknownGeometryAllowed_ChecksBothChangedObjects(double grid)
    {
        var model = Model();
        model.Page.GridSize = grid;
        model.AllowUnknownGeometry = true;
        var p = Row("Function", 1, 41, 64);
        model.Page.Placements.Add(Row("Function", 2, 61, 64));
        var line = new LayoutGuard.Segment { Locator = new() { Oid = 7 }, Changed = true, Points = [[41, 40], [60, 40]] };
        model.Lines.Add(line);
        var violations = LayoutGuard.Check(model, [p], suggest: false);
        Assert.Collection(violations, v => AssertGrid(v, p.Locator), v => AssertGrid(v, line.Locator));

        static void AssertGrid(LayoutGuard.Violation v, DrawingLocator locator)
        {
            Assert.Equal("R7", v.Rule);
            Assert.True(v.Warning);
            Assert.Same(locator, Assert.Single(v.Objects));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void R7_UnknownGridInDefaultModel_RefusesBeforeChecking(double grid)
    {
        var model = Model();
        model.Page.GridSize = grid;
        var error = Assert.Throws<BridgeErrorException>(() => LayoutGuard.Check(model, [Row("Function", 1)], suggest: false));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Fact]
    public void R7_UnreadableLineCoordinate_ReportsLine()
    {
        var model = Model();
        var line = new LayoutGuard.Segment { Changed = true, Locator = new() { Oid = 7 }, Points = [[20, 40], [double.NaN, 40]] };
        model.Lines.Add(line);
        CheckRule(model, [], "R7", true, line.Locator);
    }

    private static DrawingPlacement CrossReference(LayoutGuard.Model model, string kind = "Text")
    {
        var q = Row(kind, 2);
        q.Properties["cross_reference"] = "true";
        model.Page.Placements.Add(q);
        return q;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R9_OverlappingReference_NamesChangedAndMarkedObjectsWithConfiguredSeverity(bool strict)
    {
        var model = Model();
        model.Config.Strict = strict;
        var q = CrossReference(model);
        var p = Row("Function", 1);
        CheckRule(model, [p], "R9", true, p.Locator, q.Locator);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    public void R9_MissingOrDifferentMarker_IgnoresOverlap(string? marker)
    {
        var model = Model();
        var q = Row("Text", 2);
        model.Page.Placements.Add(q);
        if (marker is not null)
        {
            q.Properties["cross_reference"] = marker;
        }
        CheckRule(model, [Row("Function", 1)], "R9", false);
    }

    [Fact]
    public void R9_MarkerOnlyOnChangedObject_IgnoresOverlap()
    {
        var model = Model();
        model.Page.Placements.Add(Row("Text", 2));
        var p = Row("Function", 1);
        p.Properties["cross_reference"] = "true";
        CheckRule(model, [p], "R9", false);
    }

    [Fact]
    public void R9_MarkedNonTextPlacement_ReportsOverlap()
    {
        var model = Model();
        var q = CrossReference(model, "Function");
        var p = Row("Function", 1);
        CheckRule(model, [p], "R9", true, p.Locator, q.Locator);
    }

    [Fact]
    public void R9_ChangedWireThroughMarkedText_SkipsCrossReferenceRule()
    {
        var model = Model();
        CrossReference(model);
        model.Lines.Add(new() { Changed = true, Points = [[20, 42], [60, 42]] });
        CheckRule(model, [], "R9", false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void R9_ReferenceEdge_RequiresPositiveOverlapOnBothAxes(int edge)
    {
        var model = Model();
        model.Config.SymbolClearanceMm = 1;
        var q = CrossReference(model);
        var p = Row("Function", 1);
        int axis = edge % 2;
        double boundary = q.Bounds![edge];
        bool low = edge < 2;
        p.Bounds![edge] = low ? boundary - 4 : boundary + 4;
        int facing = low ? axis + 2 : axis;
        p.Bounds[facing] = low ? Math.BitDecrement(boundary) : Math.BitIncrement(boundary);
        CheckRule(model, [p], "R9", false);
        p.Bounds[facing] = boundary;
        CheckRule(model, [p], "R9", false);
        p.Bounds[facing] = low ? Math.BitIncrement(boundary) : Math.BitDecrement(boundary);
        CheckRule(model, [p], "R9", true, p.Locator, q.Locator);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new double[] { 44, 40, 40, 44 })]
    [InlineData(new double[] { 0, 0, 0, 0 })]
    public void R9_UnreadableReferenceBounds_IgnoresOverlap(double[]? bounds)
    {
        var model = Model();
        CrossReference(model).Bounds = bounds;
        CheckRule(model, [Row("Function", 1)], "R9", false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void R9_UnpositionedMember_IgnoresOverlap(bool changed)
    {
        var model = Model();
        var q = CrossReference(model);
        var p = Row("Function", 1);
        (changed ? p : q).Location = [];
        CheckRule(model, [p], "R9", false);
    }

    [Theory]
    [InlineData(40, false)]
    [InlineData(40.00000000000001, true)]
    [InlineData(44, false)]
    public void R9_UnknownChangedBounds_ReportsInsertionPointStrictlyInsideReference(double x, bool reported)
    {
        var model = Model();
        var q = CrossReference(model);
        var p = Row("Function", 1, x, 42);
        p.Bounds = null;
        CheckRule(model, [p], "R9", reported, p.Locator, q.Locator);
    }

    [Fact]
    public void R9_ReplacingMarkedObjectWithSameLocator_IgnoresOldOutline()
    {
        var model = Model();
        var q = CrossReference(model);
        var p = Row("Function", 1);
        p.Locator = q.Locator;
        CheckRule(model, [p], "R9", false);
    }

    [Fact]
    public void R9_ChangedRowsWithSameLocatorAndDifferentParents_AreExempt()
    {
        var model = Model();
        var p = Row("PropertyPlacement", 1);
        var q = Row("PropertyPlacement", 1);
        p.Parent = new() { Oid = 3 };
        q.Parent = new() { Oid = 4 };
        q.Properties["cross_reference"] = "true";
        CheckRule(model, [p, q], "R9", false);
    }

    [Fact]
    public void R9_ReferenceOwnedByChangedSymbol_IsExempt()
    {
        var model = Model();
        var p = Row("Function", 1);
        CrossReference(model).Parent = p.Locator;
        CheckRule(model, [p], "R9", false);
    }

    [Fact]
    public void R9_OwnersInSameLayoutContainer_AreExempt()
    {
        var model = Model();
        var symbol = Row("Function", 3);
        model.Page.Placements.Add(symbol);
        CrossReference(model).Parent = symbol.Locator;
        var p = Row("Function", 1);
        symbol.Properties["layout_container_owner"] = "box";
        p.Properties["layout_container_owner"] = "box";
        CheckRule(model, [p], "R9", false);
    }

    [Theory]
    [InlineData(43.999998999999995, true)]
    [InlineData(43.999999, false)]
    [InlineData(43.99999900000001, false)]
    public void R9_ContainerAroundReference_UsesContainmentTolerance(double right, bool reported)
    {
        var model = Model();
        var q = CrossReference(model);
        var p = Row("LocationBox", 1);
        p.Bounds![2] = right;
        CheckRule(model, [p], "R9", reported, p.Locator, q.Locator);
    }

    [Fact]
    public void R9_ChangedPlacementInsideMarkedContainer_IsExempt()
    {
        var model = Model();
        CrossReference(model, "LocationBox").Bounds = [36, 36, 48, 48];
        CheckRule(model, [Row("Function", 1)], "R9", false);
    }
}

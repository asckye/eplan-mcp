using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardTests
{
    [Fact]
    public void UnknownTextAtOrigin_StillChecksKnownInsertionPoint()
    {
        DrawingPlacement text = Row("Text", 1, 0, 0);
        text.Bounds = null;
        Assert.Contains(LayoutGuard.Check(Model(), [text], false), v => v.Rule == "R1");
    }

    [Fact]
    public void EmptyPropertyTextWithZeroBounds_IsNotAnUnmeasuredObjectAfterWriting()
    {
        DrawingPlacement text = Row("PropertyPlacement", 1);
        text.Bounds = [0, 0, 0, 0];
        LayoutGuard.AfterWriting(Model(), new() { Page = new() { GridSize = 4, PageSize = [100, 100], Placements = [text] } });
    }
    [Fact]
    public void MissingTextFormat_IsDeferredBeforeWriteAndOnlyWarnedAfterMeasurement()
    {
        DrawingPlacement text = Row("Text", 1);
        text.Properties.Clear();
        Assert.Empty(LayoutGuard.Check(Model(), [text], false));
        List<string> warnings = LayoutGuard.AfterWriting(Model(), new() { Page = new() { GridSize = 4, PageSize = [100, 100], Placements = [text] } });
        Assert.Contains(warnings, w => w.StartsWith("R8 unverified", StringComparison.Ordinal));
    }
    [Fact]
    public void KnownTerminalOutline_InsertingIntoWireChecksTheSplitRoutesAndOwnPinApproaches()
    {
        LayoutGuard.Model model = Model();
        model.Page.Placements = [Row("Function", 1, 40, 80), Row("Function", 2, 40, 20)];
        model.Pins = new()
        {
            ["17/1/0"] = new() { Location = [40, 80], Direction = "Down" },
            ["17/2/0"] = new() { Location = [40, 24], Direction = "Up" },
        };
        model.Lines = [new() { Points = [[40, 24], [40, 80]] }];
        DrawingPlacement terminal = LayoutGuard.Proposed("Function", [40, 48], [38, 46, 42, 50]);
        Assert.Contains(LayoutGuard.Check(model, [terminal], false), v => v.Rule == "R4");
        model.Pins["0/-1/0"] = new() { Location = [40, 50], Direction = "Up" };
        model.Pins["0/-1/1"] = new() { Location = [40, 46], Direction = "Down" };
        model.Lines = PlacementMovePlanner.Autoconnect(model.Pins).Values.Select(p => new LayoutGuard.Segment { Points = p, Changed = true }).ToList();
        Assert.Equal(2, model.Lines.Count);
        Assert.DoesNotContain(LayoutGuard.Check(model, [terminal], false), v => !v.Warning);
    }
    internal static LayoutGuard.Model Model() => new() { Page = new() { PageSize = [100, 100], GridSize = 4 } };
    internal static DrawingPlacement Row(string kind, int id, double x = 40, double y = 40) => new()
    {
        Kind = kind,
        Locator = new() { Type = 17, Oid = id, Digest = new string((char)('a' + id), 64) },
        Location = [x, y],
        Bounds = [x, y, x + 4, y + 4],
        Properties = new() { ["height_mm"] = "3.5", ["angle"] = "0", ["grouped"] = "false" }
    };
    [Theory]
    [InlineData("R1")]
    [InlineData("R2")]
    [InlineData("R3")]
    [InlineData("R4")]
    [InlineData("R5")]
    [InlineData("R6")]
    [InlineData("R7")]
    [InlineData("R8")]
    [InlineData("R9")]
    public void EveryRule_ConflictingAndFreeGeometry(string rule)
    {
        var model = Model();
        DrawingPlacement p = Row(rule is "R3" or "R8" ? "Text" : "Function", 1);
        DrawingPlacement q = Row("Function", 2);
        switch (rule)
        {
            case "R1":
                p.Bounds = [0, 40, 4, 44];
                break;
            case "R2":
                model.Page.Placements.Add(q);
                break;
            case "R3":
                model.Page.Placements.Add(q);
                break;
            case "R4":
                model.Lines.Add(new() { Points = [[20, 42], [60, 42]] });
                break;
            case "R5":
            case "R6":
                model.Lines = [new() { Points = [[20, 20], [60, 20]], Net = "A", Changed = true },
                    new() { Points = [[40, rule == "R5" ? 20 : 22], [80, rule == "R5" ? 20 : 22]], Net = "B" }];
                break;
            case "R7":
                p.Location = [41, 40];
                break;
            case "R8":
                p.Properties["angle"] = "45";
                model.SetsTextAttribute = (_, attribute) => attribute == "angle";
                break;
            case "R9":
                q.Kind = "Text";
                q.Properties["cross_reference"] = "true";
                model.Page.Placements.Add(q);
                break;
        }
        List<LayoutGuard.Violation> violations = LayoutGuard.Check(model, [p], false);
        Assert.Contains(violations, v => v.Rule == rule && v.Geometry.Length == 4 && v.Objects.Length > 0);
        Assert.All(violations.Where(v => v.Rule == rule), v => Assert.Equal(rule is "R6" or "R7" or "R9", v.Warning));
        model.Config.Strict = true;
        Assert.DoesNotContain(LayoutGuard.Check(model, [p], false), v => v.Warning);
        p.Bounds = [68, 68, 72, 72];
        p.Location = [68, 68];
        p.Properties["angle"] = "0";
        model.Lines.Clear();
        model.Page.Placements.Clear();
        Assert.Empty(LayoutGuard.Check(model, [p], false));
    }
    [Fact]
    public void FreeSearch_ReturnsNearestOnGridWithinArea_WithoutMutatingScene()
    {
        var model = Model();
        DrawingPlacement p = Row("Function", 1), blocker = Row("Function", 2);
        model.Page.Placements.Add(blocker);
        var failure = Assert.Single(LayoutGuard.Check(model, [p]));
        Assert.Equal("R2", failure.Rule);
        Assert.Equal([36d, 40d], failure.NearestFreePosition!);
        Assert.Contains("Suggested position", failure.Message, StringComparison.Ordinal);
        Assert.Empty(LayoutGuard.Check(model, [LayoutGuard.Shift(p, [-4, 0])], false));
        Assert.Equal([40d, 40d], p.Location);
        Assert.Null(LayoutGuard.FindFree(model, p, [p], 0));
    }
    [Fact]
    public void FreeSearch_FullAreaAndOversizedObject_ReturnsNoSuggestion()
    {
        var model = Model();
        model.Page.Placements.Add(new() { Kind = "Function", Locator = new() { Oid = 2 }, Location = [10, 10], Bounds = [10, 10, 90, 90] });
        Assert.Null(LayoutGuard.FindFree(model, Row("Function", 1), [Row("Function", 1)]));
        model.Page.Placements.Clear();
        DrawingPlacement giant = Row("Function", 1);
        giant.Bounds = [0, 0, 200, 200];
        Assert.Null(LayoutGuard.FindFree(model, giant, [giant]));
    }
    [Fact]
    public void R1_VerifiedAreaOverridesFallback_UnknownPageFailsClosed()
    {
        var model = Model();
        DrawingPlacement p = Row("Function", 1, 4, 4);
        Assert.Contains("Boundary fallback", LayoutGuard.Check(model, [p], false)[0].Message, StringComparison.Ordinal);
        model.DrawingArea = [0, 0, 90, 90];
        Assert.Empty(LayoutGuard.Check(model, [p], false));
        model.DrawingArea = null;
        model.Page.PageSize = null;
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeErrorException>(() => LayoutGuard.Check(model, [p])).Error.Reason);
    }
    [Fact]
    public void R2_ClearanceIsConfigurable_EdgeContactAllowedAtZero()
    {
        var model = Model();
        DrawingPlacement p = Row("Function", 1);
        model.Page.Placements.Add(Row("Function", 2, 44));
        Assert.Empty(LayoutGuard.Check(model, [p], false));
        model.Config.SymbolClearanceMm = 0.5;
        Assert.Contains(LayoutGuard.Check(model, [p], false), v => v.Rule == "R2");
    }
    [Fact]
    public void R3_TextsWithSameParentOnlyWarn_EmptyPropertyDoesNotOccupyOrigin()
    {
        var model = Model();
        DrawingPlacement p = Row("PropertyPlacement", 1), q = Row("Text", 2);
        p.Parent = q.Parent = new() { Oid = 10 };
        model.Page.Placements.Add(q);
        Assert.All(LayoutGuard.Check(model, [p], false), v => Assert.True(v.Warning));
        p.Bounds = [0, 0, 0, 0];
        Assert.Empty(LayoutGuard.Check(model, [p], false));
    }
    [Fact]
    public void R4_OnlyOwnPinApproachIsExempt_OffGridPinsAreAllowed()
    {
        var model = Model();
        DrawingPlacement p = Row("Function", 1);
        model.Page.Placements.Add(p);
        model.Pins.Add("17/1/0", new() { Location = [42.5, 42], Direction = "Right" });
        model.Lines.Add(new() { Points = [[42.5, 42], [60, 42]], Changed = true });
        Assert.DoesNotContain(LayoutGuard.Check(model, [], false), v => v.Rule is "R4");
        model.Pins.Clear();
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
    }
    [Fact]
    public void R5_R6_SameNetAndEndpointContactDoNotConflict_ZeroSpacingStillRejectsOverlap()
    {
        var model = Model();
        model.Lines = [new() { Points = [[20, 20], [60, 20]], Net = "A", Changed = true }, new() { Points = [[40, 20], [80, 20]], Net = "A" }];
        Assert.DoesNotContain(LayoutGuard.Check(model, [], false), v => v.Rule is "R5" or "R6");
        model.Lines[1].Net = "B";
        model.Config.ParallelSpacingMm = 0;
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R5");
        model.Lines[1].Points = [[60, 20], [80, 20]];
        Assert.DoesNotContain(LayoutGuard.Check(model, [], false), v => v.Rule is "R5" or "R6");
    }
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void R8_QuarterTurnsAndConfiguredHeightAreAccepted(int angle)
    {
        var model = Model();
        DrawingPlacement p = Row("Text", 1);
        model.SetsTextAttribute = (_, attribute) => attribute == "height_mm";
        p.Properties["angle"] = angle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        model.Config.MinimumTextHeightMm = 3.5;
        Assert.Empty(LayoutGuard.Check(model, [p], false));
        model.Config.MinimumTextHeightMm = 4;
        Assert.Contains(LayoutGuard.Check(model, [p], false), v => v.Rule == "R8");
    }
    [Fact]
    public void LineRectangleIntersection_UsesSegmentRatherThanBoundingBox()
    {
        var model = Model();
        model.Lines.Add(new() { Points = [[20, 20], [80, 80]] });
        DrawingPlacement p = Row("Text", 1, 20, 60);
        Assert.Empty(LayoutGuard.Check(model, [p], false));
    }
    [Fact]
    public void InvalidConfigurationRefuses_UnknownPredictionDefersBounds()
    {
        var model = Model();
        model.Config.MarginMm = double.NaN;
        Assert.Throws<BridgeErrorException>(() => LayoutGuard.Check(model, [], false));
        model.Config.MarginMm = 10;
        DrawingPlacement p = Row("Text", 1);
        p.Bounds = null;
        Assert.Empty(LayoutGuard.Check(model, [p], false));
    }
}

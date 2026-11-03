using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardRegressionTests
{
    [Theory]
    [InlineData(-90)]
    [InlineData(90.00000000000001)]
    [InlineData(359.9999995)]
    [InlineData(450)]
    public void ExplicitAngle_NormalizedQuarterTurnsUseTolerance(double angle)
    {
        var model = Model();
        model.SetsTextAttribute = (_, attribute) => attribute == "angle";
        DrawingPlacement text = LayoutGuardTests.Row("Text", 1);
        text.Properties["angle"] = angle.ToString("R", CultureInfo.InvariantCulture);
        Assert.Empty(LayoutGuard.Check(model, [text], false));
        text.Properties["angle"] = "90.000002";
        Assert.Contains(LayoutGuard.Check(model, [text], false), v => v.Rule == "R8" && !v.Warning);
    }

    [Theory]
    [InlineData("height_mm", "3.5", "45", true)]
    [InlineData("angle", "0.25", "90", true)]
    [InlineData("height_mm", "0.25", "90", false)]
    [InlineData("angle", "3.5", "45", false)]
    [InlineData("", "0.25", "45", true)]
    public void R8_OnlyTheOffendingAttributeSetByThisCallCanRefuse(string attribute, string height, string angle, bool warning)
    {
        var model = Model();
        model.Config.Strict = true;
        model.SetsTextAttribute = (_, name) => name == attribute;
        DrawingPlacement text = LayoutGuardTests.Row("PropertyPlacement", 1);
        text.Properties["height_mm"] = height;
        text.Properties["angle"] = angle;
        LayoutGuard.Violation violation = Assert.Single(LayoutGuard.Check(model, [text], false));
        Assert.Equal(("R8", warning), (violation.Rule, violation.Warning));
    }

    [Theory]
    [InlineData("from_layer", "from_layer")]
    [InlineData("-16002", "-16002")]
    [InlineData("0", "unknown")]
    public void UnknownFormat_OnlyAppearsInLayoutChecks(string height, string angle)
    {
        var model = Model();
        model.SetsTextAttribute = (_, _) => true;
        DrawingPlacement text = LayoutGuardTests.Row("Text", 1, 0, 0);
        text.Properties["height_mm"] = height;
        text.Properties["angle"] = angle;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => LayoutGuard.Enforce(model, [text])).Error;
        Assert.Contains("R1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("R8:", error.Message, StringComparison.Ordinal);
        Assert.Contains("R8 unverified", error.Metadata!["layoutChecks"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PLC")]
    [InlineData("LocationBox")]
    [InlineData("Box")]
    public void Logged10232TextsAnd69840Outline_AreContainerContents(string kind)
    {
        var model = Model();
        DrawingPlacement box = LayoutGuardTests.Row(kind, 10232, 80, 140);
        box.Locator.Type = 45;
        box.Bounds = [80, 140, 120, 192]; // Fixture container around the logged outline and property text bounds.
        DrawingPlacement function = LayoutGuardTests.Row("Function", 69840, 80, 160);
        function.Bounds = [80, 152, 120, 181.25];
        double[][] bounds = [[95.275, 160.9193, 104.725, 164], [88.525, 156.9193, 111.475, 160], [97.3, 164.9193, 102.7, 168], [96.419299999999993, 184, 99.5, 190.75]];
        DrawingPlacement[] texts = bounds.Select((b, i) =>
        {
            DrawingPlacement row = LayoutGuardTests.Row("PropertyPlacement", 10232, b[0], b[1]);
            row.Locator.Type = 45;
            row.Locator.Digest = new string((char)('a' + i), 64);
            row.Parent = box.Locator;
            row.Bounds = b;
            row.Properties["angle"] = "from_layer";
            return row;
        }).ToArray();
        model.Page.Placements = [box, .. texts];
        Assert.DoesNotContain(LayoutGuard.Check(model, [function], false), v => v.Rule is "R2" or "R3");
        double[] measuredBounds = function.Bounds;
        function.Bounds = null;
        Assert.DoesNotContain(LayoutGuard.Check(model, [function], false), v => v.Rule is "R2" or "R3");
        function.Bounds = measuredBounds;
        var actual = Model();
        actual.Page.Placements = [box, .. texts, function];
        Assert.NotEmpty(LayoutGuard.AfterWriting(model, actual));
        actual.Lines = [new() { Points = [[84, 162], [116, 162]], Changed = true }];
        Assert.DoesNotContain(LayoutGuard.Check(actual, [], false), v => v.Rule == "R3");
        actual.Lines.Clear();
        // A second unrelated function inside that same box must still conflict with the first.
        DrawingPlacement other = LayoutGuardTests.Row("Function", 69841, 84, 160);
        Assert.Contains(LayoutGuard.Check(actual, [other], false), v => v.Rule == "R2" && !v.Warning);
        function.Bounds = [116, 152, 124, 181.25];
        Assert.Contains(LayoutGuard.Check(model, [function], false), v => v.Rule == "R2" && !v.Warning);
    }

    [Fact]
    public void Logged69888OwnTextPair_OnlyWarns_AndUnrelatedDeviceIsADeviationWithFreePosition()
    {
        var before = Model();
        DrawingPlacement owner = LayoutGuardTests.Row("Function", 69888, 80, 208);
        DrawingPlacement first = LayoutGuardTests.Row("PropertyPlacement", 69888, 77, 208);
        first.Parent = owner.Locator;
        first.Bounds = [77, 207.7521004456824, 88.253411666478613, 216.25];
        first.Locator.Digest = "ef41444470a2a1806e61874950b83d93ae4bc3bacd3fe78dd2b87d3e28e0b433";
        DrawingPlacement second = LayoutGuardTests.Row("PropertyPlacement", 69888, 84, 208);
        second.Parent = owner.Locator;
        second.Bounds = [84.3193, 205.7, 87.4, 208.4];
        second.Locator.Digest = "8e8664ac8364fdf0329b9fb2b75fe63b5e7af84ce5156313059fe06118c6bc7e";
        second.Properties["angle"] = "from_layer";
        var actual = Model();
        actual.Page.Placements = [owner, first, second];
        List<string> checks = LayoutGuard.AfterWriting(before, actual);
        Assert.Contains(checks, s => s.StartsWith("R3: Warning:", StringComparison.Ordinal));
        Assert.All(LayoutGuard.Check(actual, actual.Page.Placements, false), v => Assert.True(v.Warning));
        DrawingPlacement blocker = LayoutGuardTests.Row("Function", 7, 80, 208);
        before.Page.Placements.Add(blocker);
        actual.Page.Placements.Insert(0, blocker);
        // ADR-0046: after the write the overlap with an unrelated device is a committed deviation with a free position.
        checks = LayoutGuard.AfterWriting(before, actual);
        Assert.Contains(checks, s => s.StartsWith("Readback deviation: R2", StringComparison.Ordinal) && s.Contains("Suggested position", StringComparison.Ordinal));
        Assert.DoesNotContain(checks, s => s.StartsWith("Readback deviation: R3", StringComparison.Ordinal));
    }

    [Fact]
    public void UnchangedExistingOverlap_DoesNotBlockNewDevice()
    {
        DrawingPlacement a = LayoutGuardTests.Row("Text", 1), b = LayoutGuardTests.Row("Function", 2);
        var before = Model();
        before.Page.Placements = [a, b];
        var actual = Model();
        actual.Page.Placements = [a, b, LayoutGuardTests.Row("Function", 3, 140, 100)];
        Assert.DoesNotContain(LayoutGuard.AfterWriting(before, actual), s => s.StartsWith("R3:", StringComparison.Ordinal));
    }

    private static LayoutGuard.Model Model() => new() { Page = new() { PageSize = [420, 297], GridSize = 4 } };
}

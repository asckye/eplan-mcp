using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardDefaultTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("height_mm")]
    [InlineData("angle")]
    public void LiveW4_DefaultTypeTextAcross248Wire_WarnsWithOwnerAndIndependentFreePosition(string? attribute)
    {
        var before = Model();
        before.Config.Strict = true;
        if (attribute is not null)
        { before.SetsTextAttribute = (_, name) => name == attribute; }
        LayoutGuard.Model actual = Model();
        actual.Config.Strict = true;
        DrawingPlacement text = CableText(actual);

        List<string> checks = LayoutGuard.AfterWriting(before, actual);

        string warning = Assert.Single(checks, s => s.StartsWith("R3: Warning:", StringComparison.Ordinal));
        Assert.Contains("Text " + text.Locator.Type + "/" + text.Locator.Oid, warning, StringComparison.Ordinal);
        Assert.Contains("owned by 17/69867", warning, StringComparison.Ordinal);
        Assert.Contains("Line 0/-37", warning, StringComparison.Ordinal);
        Assert.Contains("eplan_placement_update property_moves", warning, StringComparison.Ordinal);
        LayoutGuard.Violation conflict = Assert.Single(LayoutGuard.Check(actual, [actual.Page.Placements[0], text]));
        Assert.True(conflict.Warning);
        double[] free = Assert.IsType<double[]>(conflict.NearestFreePosition);
        Assert.Contains("Suggested position for property_moves", warning, StringComparison.Ordinal);
        Assert.False(text.Location.SequenceEqual(free));
        actual.SetsTextAttribute = (_, name) => name == "position";
        Assert.DoesNotContain(LayoutGuard.Check(actual, [LayoutGuard.Shift(text, free.Zip(text.Location, (a, b) => a - b).ToArray())], false), v => v.Rule == "R3");
        Assert.Equal([223.12, 96.28, 272, 100.56], text.Bounds!);
        Assert.Equal([272d, 92d], actual.Page.Placements[0].Location);
    }

    [Fact]
    public void DefaultText_NoFreePositionWithinDrawingArea_StillOnlyWarns()
    {
        LayoutGuard.Model actual = Model();
        DrawingPlacement text = CableText(actual);
        actual.DrawingArea = [220, 64, 290, 120];
        LayoutGuard.Violation conflict = Assert.Single(LayoutGuard.Check(actual, [text]));
        Assert.True(conflict.Warning);
        Assert.Null(conflict.NearestFreePosition);
        Assert.Contains("No free position found within the bounded search", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("eplan_placement_update property_moves", conflict.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Function")]
    [InlineData("Text")]
    [InlineData("Line")]
    public void DefaultText_AgainstUnrelatedPlacement_WarnsButExplicitPositionRefuses(string kind)
    {
        LayoutGuard.Model model = Model();
        DrawingPlacement text = CableText(model);
        model.Lines.Clear();
        DrawingPlacement obstacle = LayoutGuardTests.Row(kind, 9, 248, 100);
        obstacle.Bounds = [244, 96, 252, 104];
        model.Page.Placements.Add(obstacle);
        LayoutGuard.Violation warning = Assert.Single(LayoutGuard.Check(model, [text], false));
        Assert.True(warning.Warning);
        Assert.Contains(kind + " 17/9", warning.Message, StringComparison.Ordinal);
        model.SetsTextAttribute = (_, name) => name == "position";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => LayoutGuard.Enforce(model, [text])).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("Text bounds conflict with another text, symbol or line.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedOwner_UnchangedDefaultTextAgainstChangedObject_WarnsInBothDirections()
    {
        LayoutGuard.Model model = Model();
        DrawingPlacement text = CableText(model);
        model.Lines.Clear();
        DrawingPlacement owner = model.Page.Placements[0];
        DrawingPlacement obstacle = LayoutGuardTests.Row("Function", 9, 248, 100);
        obstacle.Bounds = [244, 96, 252, 104];
        model.Page.Placements.Add(obstacle);
        Assert.All(LayoutGuard.Check(model, [owner, obstacle], false), v => Assert.True(v.Warning));
        Assert.Contains(LayoutGuard.Check(model, [owner, obstacle], false), v => v.Rule == "R3" && v.Objects[0] == text.Locator);
        // Moving an unrelated object onto an unchanged text still refuses.
        Assert.Contains(LayoutGuard.Check(model, [obstacle], false), v => v.Rule == "R3" && !v.Warning);
    }

    [Fact]
    public void ChangedLine_AgainstUnchangedDefaultText_StillRefuses()
    {
        LayoutGuard.Model model = Model();
        CableText(model);
        model.Lines[0].Changed = true;
        Assert.Contains(LayoutGuard.Check(model, [], false), v => v.Rule == "R3" && !v.Warning);
    }

    internal static LayoutGuard.Model Model() => new()
    {
        Page = new() { GridSize = 4, PageSize = [420, 297] },
        Lines = [new() { Locator = new() { Oid = -37 }, Points = [[248, 64], [248, 120]] }]
    };

    internal static DrawingPlacement CableText(LayoutGuard.Model model)
    {
        DrawingPlacement cable = LayoutGuardTests.Row("Cable", 69867, 272, 92);
        cable.Bounds = [272, 92, 288, 92];
        DrawingPlacement text = LayoutGuardTests.Row("PropertyPlacement", 69867, 272, 92);
        text.Locator.Digest = new string('b', 64);
        text.Parent = cable.Locator;
        text.Bounds = [223.12, 96.28, 272, 100.56];
        text.Properties["display"] = "Fictional M1 control cable";
        text.Properties["property_id"] = "20040";
        text.Properties["property_index"] = "4";
        model.Page.Placements = [cable, text];
        return text;
    }
}

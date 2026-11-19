using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementDeleteReadbackTests
{
    private static DrawingPlacement Row(long oid, string digest, string kind = "Line", DrawingPlacement? parent = null) =>
        new() { Kind = kind, Locator = new() { Type = 4, Oid = oid, Digest = digest }, Parent = parent?.Locator };

    [Fact]
    public void MatchesRepeatedIdentitiesAndChildDigestsAsMultisets()
    {
        DrawingPlacement old = Row(1, "a");
        DrawingPlacement next = Row(1, "a");
        DrawingPlacement unchanged = Row(1, "b");
        DrawingPlacement missing = Row(1, "c");
        var diff = PlacementDeleteReadback.Compare(
            [old, unchanged, missing, Row(1, "x", parent: old), Row(1, "x", parent: old), Row(1, "y", parent: old)],
            [unchanged, next, Row(1, "y", parent: next), Row(1, "x", parent: next)], [], false);
        Assert.Equal((next, "changed"), Assert.Single(diff.Changed));
        Assert.Same(missing, Assert.Single(diff.Missing));
    }

    [Fact]
    public void ReorderingChildrenDoesNotChangeRootAndVanishedRootCountsOnce()
    {
        DrawingPlacement root = Row(2, "r");
        DrawingPlacement first = Row(2, "a", parent: root);
        DrawingPlacement second = Row(2, "b", parent: root);
        Assert.Empty(PlacementDeleteReadback.Compare([root, first, second], [second, root, first], [], false).Changed);
        Assert.Same(root, Assert.Single(PlacementDeleteReadback.Compare([root, first, second], [], [], false).Missing));
    }

    [Fact]
    public void DuplicateSurplusAfterIsAdded()
    {
        DrawingPlacement old = Row(1, "a");
        DrawingPlacement replacement = Row(1, "b");
        DrawingPlacement extra = Row(1, "c");
        var diff = PlacementDeleteReadback.Compare([old], [replacement, extra], [], false);
        Assert.Equal([(replacement, "changed"), (extra, "added")], diff.Changed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyCableTargetsAllowMissingDefinitionPoints(bool hasCable)
    {
        DrawingPlacement point = Row(3, "x", "ConnectionDefinitionPoint");
        DrawingPlacement target = Row(1, "t", hasCable ? "Cable" : "Function");
        var diff = PlacementDeleteReadback.Compare([point, Row(4, "y", "Connection")], [], [target], hasCable);
        Assert.Equal(hasCable ? [point] : Array.Empty<DrawingPlacement>(), diff.AlsoRemoved);
        Assert.Equal(hasCable ? Array.Empty<DrawingPlacement>() : [point], diff.Missing);
    }

    [Fact]
    public void AmbiguousSurplusOfSameIdentityCanBeACablePoint()
    {
        DrawingPlacement point = Row(3, "x", "ConnectionDefinitionPoint");
        DrawingPlacement graphic = Row(3, "y");
        DrawingPlacement updated = Row(3, "z");
        var diff = PlacementDeleteReadback.Compare([point, graphic], [updated], [Row(1, "c", "Cable")], true);
        Assert.Empty(diff.Missing);
        Assert.Same(point, Assert.Single(diff.AlsoRemoved));
        Assert.Equal((updated, "changed"), Assert.Single(diff.Changed));
    }

    [Fact]
    public void ErrorListsRemainingBeforeMissingAndCapsTheirCombinedCount()
    {
        DrawingPlacement target = Row(1, "a", "Cable");
        DrawingPlacement child = Row(2, "b", "PropertyPlacement", target);
        DrawingPlacement reclassified = Row(1, "z", "Connection");
        var expected = Enumerable.Range(10, 10).Select(i => Row(i, "a")).ToList();
        expected[0].Properties["name"] = "Q1";
        var diff = PlacementDeleteReadback.Compare(expected, [reclassified, child], [target], true);
        Assert.Equal([reclassified, child], diff.Remaining);
        string error = Assert.Throws<BridgeErrorException>(diff.Verify).Message;
        Assert.Contains("remaining: Connection 4/1; remaining: PropertyPlacement 4/2; missing: Line 4/10 Q1", error, StringComparison.Ordinal);
        Assert.EndsWith("missing: Line 4/17 and 2 more", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    public void ChangedCapRetainsTotalAndAppendsWarning(int count)
    {
        var diff = PlacementDeleteReadback.Compare([], Enumerable.Range(1, count).Select(i => Row(i, "x")).ToList(), [], false);
        var result = new JObject();
        diff.AddTo(result, "Undo warning.");
        Assert.Equal(20, ((JArray)result["changed"]!).Count);
        Assert.Equal(count > 20 ? count : (int?)null, (int?)result["changedTotal"]);
        Assert.Equal($"Undo warning. EPLAN also updated {count} other object(s) on the page; see changed.", (string?)result["warning"]);
    }
}

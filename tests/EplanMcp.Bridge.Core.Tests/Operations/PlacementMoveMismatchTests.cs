using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementMoveMismatchTests
{
    [Fact]
    public void CountMismatch_ListsAreLimitedToTwentyAndTotalsRetainEveryPlacement()
    {
        var expected = new ConnectionCreatePlanner.Scene();
        var actual = new ConnectionCreatePlanner.Scene();
        for (int i = 0; i < 22; i++)
        { actual.Page.Placements.Add(new() { Kind = "Text", Locator = new() { Type = 45, Oid = i + 100 } }); }
        for (int i = 0; i < 21; i++)
        { expected.Page.Placements.Add(new() { Kind = "Function", Locator = new() { Type = 17, Oid = i + 1 } }); }

        BridgeError error = PlacementMoveMismatch.Create("count", "placement count", expected, expected, actual,
            actual.Page.Placements.Select(PlacementMoveMismatch.Describe).ToArray(), 21, 22).Error;

        JObject mismatch = JObject.Parse(error.Metadata!["moveMismatch"]);
        Assert.Equal((21, 22, 22, 21), ((int)mismatch["expectedCount"]!, (int)mismatch["actualCount"]!, (int)mismatch["addedTotal"]!, (int)mismatch["removedTotal"]!));
        Assert.Equal(20, mismatch["added"]!.Count());
        Assert.Equal(20, mismatch["removed"]!.Count());
        Assert.Equal(119L, (long)mismatch["added"]![19]!["oid"]!);
        Assert.Equal(20L, (long)mismatch["removed"]![19]!["oid"]!);
        Assert.Equal(JTokenType.Null, mismatch["added"]![0]!["bounds"]!.Type);
        Assert.Null(mismatch["added"]![0]!["name"]);
    }
}

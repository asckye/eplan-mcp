using System.Text.Json;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PlacementStyleTests
{
    [Theory]
    [InlineData("\"color\":-16002")]
    [InlineData("\"width_mm\":-16002")]
    [InlineData("\"style\":-16002")]
    [InlineData("\"pattern_length\":-16002")]
    public void PenDefaultSentinel_IsAcceptedAndForwarded(string field) => AssertPenForwarded(field, -16002);

    [Fact]
    public void NegativeColor_FromTheSample_IsAcceptedAndForwarded() => AssertPenForwarded("\"color\":-5", -5);

    private static void AssertPenForwarded(string field, double expected)
    {
        JsonElement input = PlacementCreateTests.Input("{\"kind\":\"line\",\"points\":[[20,20],[40,40]],\"pen\":{" + field + "},\"layer\":3}");
        Assert.Empty(PlacementCreatePlanning.Validate(input));
        Assert.True(PropertyGetContractTests.Accepts(PlacementCreateContract.Input, input, PlacementCreateContract.Input));
        Assert.Equal(expected, Assert.Single(PlacementCreatePlanning.Request(input.GetProperty("items")[0]).Pen!).Value);
    }

    [Theory]
    [InlineData("\"pen\":{\"style_factor\":2}")]
    [InlineData("\"pen\":{\"style\":40000}")]
    [InlineData("\"pen\":{\"color\":1.5}")]
    [InlineData("\"pen\":{\"line_end\":3}")]
    [InlineData("\"pen\":{\"line_end\":-16002}")]
    [InlineData("\"pen\":{\"width_mm\":null}")]
    [InlineData("\"layer\":\"3\"")]
    [InlineData("\"layer\":-1")]
    [InlineData("\"angle\":null")]
    [InlineData("\"closed\":1")]
    [InlineData("\"alignment\":\"middleleft\"")]
    public void InvalidStyle_IsRejected(string fields)
    {
        JsonElement input = PlacementCreateTests.Input("{\"kind\":\"polyline\",\"points\":[[20,20],[40,40]]," + fields + "}");
        Assert.NotEmpty(PlacementCreatePlanning.Validate(input));
    }
}
[Trait("Size", "Medium")]
public sealed class PlacementStylePreviewTests
{
    [Fact]
    public async Task Preview_PreservesAllStyleValuesWithoutWrites()
    {
        JsonElement input = PlacementCreateTests.Input("""{"kind":"text","position":[20,20],"height_mm":3.5,"contents":{"??_??":"Style"},"angle":90,"alignment":"MiddleCenter","layer":3,"pen":{"color":-16002,"width_mm":0.25,"style":1,"pattern_length":8,"line_end":1}}""");
        JsonElement result = PlacementCreateTests.Data(await PlacementCreateTests.Preview(PlacementCreateTests.Bridge(), input));
        Assert.Equal(90, result.GetProperty("items")[0].GetProperty("actual").GetProperty("angle").GetDouble());
        Assert.Equal("MiddleCenter", result.GetProperty("items")[0].GetProperty("actual").GetProperty("alignment").GetString());
        Assert.Equal(5, result.GetProperty("items")[0].GetProperty("actual").GetProperty("pen").EnumerateObject().Count());
    }
}

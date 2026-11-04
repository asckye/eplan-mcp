using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class LayoutGuardContractTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("\"ignore\"")]
    public void CreateRejectsInvalidGuardBeforeReading(string value)
    {
        JsonElement input = JsonSerializer.Deserialize<JsonElement>(
            "{\"project\":\"P\",\"page\":\"1\",\"items\":[{\"kind\":\"line\",\"points\":[[10,10],[20,10]]}],\"layout_guard\":" + value + "}");
        Assert.Contains(PlacementCreatePlanning.Validate(input), p => p.StartsWith("/layout_guard:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlacementSchemasDeclareSameClosedGuardAndDefault(bool update)
    {
        JsonElement schema = update ? PlacementUpdateContract.Input : PlacementCreateContract.Input;
        JsonElement guard = schema.GetProperty("properties").GetProperty("layout_guard");
        Assert.Equal("refuse", guard.GetProperty("default").GetString());
        Assert.Equal(["refuse", "warn"], guard.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }
}

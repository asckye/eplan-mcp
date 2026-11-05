using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionArrangementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("左,0°")]
    [InlineData("默认 (右,0°)")]
    public void ExactLocalizedName_IsPreservedWithExplicitVariant(string? arrangement)
    {
        var fixture = Fixture();
        if (arrangement is not null)
        { fixture.Request["items"]![0]!["property_arrangement"] = arrangement; }
        JToken item = Assert.Single(fixture.Plan()["symbols"]!);
        Assert.Equal(arrangement, (string?)item["interruption"]!["property_arrangement"]);
        Assert.Equal(0, (int)item["symbol"]!["variant"]!);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    [InlineData("\"left\\n0\"")]
    public void InvalidName_RefusesBeforeWriting(string json)
    {
        var fixture = Fixture();
        fixture.Request["items"]![0]!["property_arrangement"] = JToken.Parse(json);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("property_arrangement", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    private static ConnectionCreateFixture Fixture()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request["generate"] = false;
        fixture.Symbols.AddRange(InterruptionPointFixture.BpVariants());
        fixture.Request["items"] = new JArray(new JObject
        {
            ["kind"] = "interruption_point",
            ["name"] = "=GAB1-24V",
            ["symbol"] = "BP",
            ["symbol_variant"] = 0,
            ["direction"] = "Right",
            ["position"] = new JArray(60, 60)
        });
        return fixture;
    }
}

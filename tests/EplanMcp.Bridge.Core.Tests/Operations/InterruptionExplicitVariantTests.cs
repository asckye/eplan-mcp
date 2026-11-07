using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionExplicitVariantTests
{
    [Fact]
    public void ExampleRightBp_ExplicitZeroOverridesIncomingDefault_OmissionTakesE()
    {
        var f = Fixture();
        Assert.Equal(4, (int)Assert.Single(f.Plan()["symbols"]!)["symbol"]!["variant"]!);
        f.Request["items"]![0]!["symbol_variant"] = 0;
        Assert.Equal(0, (int)Assert.Single(f.Plan()["symbols"]!)["symbol"]!["variant"]!);
        Assert.Equal("Right", (string?)Assert.Single(f.Plan()["symbols"]!)["interruption"]!["direction"]);
    }

    [Fact]
    public void ScopedBpZero_UsesRequestedRightPinInsteadOfUnconnectedLastPin()
    {
        var f = new InterruptionPointFixture();
        f.Request["items"]!.Last!["symbol_variant"] = 0;
        f.Request["items"]!.Last!["direction"] = "Right";
        JObject result = f.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Single(f.Placement.Calls, c => c == "CloseUndo");
    }

    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("0.5")]
    [InlineData("\"0\"")]
    [InlineData("2147483648")]
    public void MissingVariantOrDirectionAndInvalidTypes_AreInvalidArguments(string value)
    {
        var f = Fixture();
        f.Request["items"]![0]!["symbol_variant"] = JToken.Parse(value);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => f.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("/items/0/symbol_variant", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    private static ConnectionCreateFixture Fixture()
    {
        var f = new ConnectionCreateFixture();
        f.Scene.Page.Page = "=GAA+A1&EFS1/2";
        f.Request["page"] = f.Scene.Page.Page;
        f.Request["generate"] = false;
        f.Symbols.AddRange(InterruptionPointFixture.BpVariants());
        f.Request["items"] = new JArray(new JObject
        {
            ["kind"] = "interruption_point",
            ["name"] = "=GAA-2L1",
            ["symbol"] = "BP",
            ["direction"] = "Right",
            ["position"] = new JArray(60, 60),
        });
        return f;
    }
}

using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class UnpairedInterruptionTests
{
    [Fact]
    public void OmittedCounterpartPages_PlacesOnly_WithoutScopeOrGeneration()
    {
        var fixture = new InterruptionPointFixture { Isolated = true, GeneratedEndpoints = [] };
        JObject item = (JObject)fixture.Request["items"]!.Last!;
        item.Remove("counterpart_pages");
        fixture.Request["items"] = new JArray(item);
        JObject plan = fixture.Plan();
        Assert.Null(plan["interruptionScope"]);
        Assert.Equal(1, (int)plan["undoSteps"]!);
        Assert.Equal(ExecutionState.Committed, (string?)fixture.Execute()["execution"]);
        Assert.Empty(fixture.ScopeReads);
        Assert.Empty(fixture.Generates);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[1]")]
    [InlineData("[\"same\",\"same\"]")]
    public void SuppliedInvalidCounterparts_StillRefuse(string pages)
    {
        var fixture = new InterruptionPointFixture();
        fixture.Request["items"]!.Last!["counterpart_pages"] = JToken.Parse(pages);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error.Reason);
        Assert.Empty(fixture.ScopeReads);
    }
}

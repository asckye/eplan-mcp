using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class ApiTryOperationTests
{
    [Theory]
    [InlineData("O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate", "{}")]
    [InlineData("O:Eplan.EplApi.DataModel.MacroBox.RepresentationType", "{\"value\":null}")]
    [InlineData("O:Eplan.EplApi.DataModel.Page.ConvertToGraphics", "{}")]
    public void RefusedWriteNeverResolvesATargetOrEntersATransaction(string group, string parameters)
    {
        ApiTryArgs args = Args(group);
        args.Project = "not-open";
        args.Parameters = parameters;
        _dispatch.TargetCheck = _ => throw new InvalidOperationException("Refusals must precede target resolution.");
        foreach (Action invoke in new Action[] { () => Plan(args), () => Executor().Execute(args) })
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(invoke).Error;
            Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (error.Reason, error.Execution));
            Assert.Contains("crashed EPLAN in a live trial (api-notes A77)", error.Message, StringComparison.Ordinal);
            Assert.Contains("nothing changed", error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(0, _dispatch.Invocations);
        Assert.Equal(0, _dispatch.Inspections);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void MacroBoxRepresentationReadRemainsDispatchable()
    {
        WritePlan plan = Plan(Args("O:Eplan.EplApi.DataModel.MacroBox.RepresentationType"));
        Assert.Equal("read", Assert.Single(plan.Changes).Action);
        Assert.Equal(0, _dispatch.Invocations);
    }
}

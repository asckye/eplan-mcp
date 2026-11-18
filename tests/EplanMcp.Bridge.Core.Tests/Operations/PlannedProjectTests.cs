using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>WriteArguments.RequirePlanned: the approved project path against the one the request's name resolves to.</summary>
[Trait("Size", "Small")]
public sealed class PlannedProjectTests
{
    private const string Resolved = @"D:\Projects\Line1";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"D:\Projects\Line1")]
    [InlineData("d:/projects/line1")]
    public void SamePathOrNoPlannedPath_Passes(string? planned) => WriteArguments.RequirePlanned(planned, Resolved);

    [Theory]
    [InlineData(@"D:\Scratch\Line1")]
    [InlineData("Line1")]
    public void AnotherProject_IsStaleAndNotStarted(string planned)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => WriteArguments.RequirePlanned(planned, Resolved));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }
}

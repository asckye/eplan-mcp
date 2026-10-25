using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PlanStepGuardBoundsTests
{
    [Fact]
    public void Guard_ExactlyTenThousandObjects_Passes()
    {
        using var reader = new PlanGuardReader();
        reader.Content[1].AddRange(Enumerable.Range(3, 9998).Select(i => new SnapshotSourceObject
        {
            Kind = "function",
            Identifier = $"28/17/{i}/0",
            Oid = i,
            Page = "+A1/1",
        }));
        PlanStepGuardArgs args = reader.Args();
        Assert.Equal(10_000, args.Preconditions[0].Count);
        Assert.Equal("ok", new PlanStepGuardOperation(reader, reader, reader, reader, () => 0).Execute(args));
    }

    [Fact]
    public void Guard_FiftyPagesPass_FiftyFirstLivePageInStructureIsRejected()
    {
        using var reader = new PlanGuardReader();
        for (int i = 2; i <= 50; i++)
        {
            reader.Content[i] = [new() { Kind = "page", Identifier = $"28/4/{i}/0", Oid = i, Name = "+A1/" + i }];
        }
        PlanStepGuardArgs args = reader.Args();
        args.ReadScope.Pages.Clear();
        args.ReadScope.Structures = ["+A1"];
        var operation = new PlanStepGuardOperation(reader, reader, reader, reader, () => 0);
        Assert.Equal("ok", operation.Execute(args));
        reader.Content[51] = [new() { Kind = "page", Identifier = "28/4/51/0", Oid = 51, Name = "+A1/51" }];
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (failure.Reason, failure.Execution));
        Assert.Equal(50, reader.Reads);
    }

    [Fact]
    public void Guard_NameDomainAndReadScopeTogetherExceedObjectBound_IsRejected()
    {
        using var reader = new PlanGuardReader();
        PlanStepGuardArgs args = reader.Args(all: true);
        reader.Names.AddRange(Enumerable.Range(2, 10_000).Select(i => new PlanFunction
        {
            Type = 17,
            Oid = i,
            Name = "+A1-K" + i,
        }));
        BridgeError failure = Assert.Throws<BridgeErrorException>(() =>
            new PlanStepGuardOperation(reader, reader, reader, reader, () => 0).Execute(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, failure.Reason);
    }
}

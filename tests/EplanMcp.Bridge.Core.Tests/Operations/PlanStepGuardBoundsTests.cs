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
    public void Guard_ReadScopeOverFiftyPages_IsReadInFull()
    {
        // Collision domains name every page of the project (design 4.3); only modify scopes are limited to 50 pages.
        using var reader = new PlanGuardReader();
        for (int i = 2; i <= 120; i++)
        {
            reader.Content[i] = [new() { Kind = "page", Identifier = $"28/4/{i}/0", Oid = i, Name = "+A1/" + i }];
        }
        PlanStepGuardArgs args = reader.Args();
        args.ReadScope.Pages.Clear();
        args.ReadScope.Structures = ["+A1"];
        var operation = new PlanStepGuardOperation(reader, reader, reader, reader, () => 0);
        Assert.Equal("ok", operation.Execute(args));
        Assert.Equal(120, reader.Reads);
    }

    [Fact]
    public void Guard_OverTenThousandNamesWithSmallContent_Passes()
    {
        using var reader = new PlanGuardReader();
        PlanStepGuardArgs args = reader.Args(all: true);
        args.Preconditions.Single(p => p.Tag == "absent" && p.Kind == "function").Name = "+A1-FRESH";
        reader.Names.AddRange(Enumerable.Range(2, 10_000).Select(i => new PlanFunction
        {
            Type = 17,
            Oid = i,
            Name = "+A1-K" + i,
        }));
        Assert.Equal("ok", new PlanStepGuardOperation(reader, reader, reader, reader, () => 0).Execute(args));
        Assert.Equal(1, reader.NameReads);
    }

    [Fact]
    public void Guard_PageCreate_OnlyReadsFreshPageListWithoutContentsOrFunctionEnumeration()
    {
        using var reader = new PlanGuardReader();
        var scope = new PlanGuardScope();
        var args = new PlanStepGuardArgs
        {
            Project = reader.Path,
            ReadScope = scope,
            Preconditions =
            [
                new() { Tag = "members", Scope = scope, Hash = PlanGuardReader.MemberHash(reader.Path, []), Count = 0 },
                new() { Tag = "absent", Kind = "page", Name = "+A1/9" },
            ],
        };
        var dispatcher = new InlineDispatcher();
        var operation = new PlanStepGuardOperation(reader, reader, reader, reader, () => reader.Milliseconds);
        var router = new OperationRouter([operation], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(10));
        Assert.Equal("ok", (string?)router.Invoke(OperationNames.PlanStepGuard,
            EplanMcp.Bridge.Core.Protocol.BridgeJson.FromObject(args), "page-create"));
        Assert.Equal((1, 0, 0, 0, 1), (reader.Opens, reader.Reads, reader.Finds, reader.NameReads, dispatcher.Calls));

        args.Preconditions[1].Name = "+a1/1";
        BridgeError failure = Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (failure.Reason, failure.Execution));
        Assert.Equal((0, 0, 0), (reader.Reads, reader.Finds, reader.NameReads));
    }

    [Fact]
    public void Guard_MergedFallbackSharesContentBudget_StopsAtCheckpointAndNeverStartsWrite()
    {
        using var h = new PlanStepHarness();
        PlanStepExecuteArgs args = h.Step("device.update");
        args.Guard = h.GuardReader.Args(all: true);
        h.GuardReader.PerRead = 4_000;
        int batches = 0;
        h.GuardReader.GuardScan = checkpoint =>
        {
            for (int i = 0; i < 3; i++)
            {
                batches++;
                h.GuardReader.Milliseconds += 3_000;
                checkpoint();
            }
        };

        BridgeError failure = Assert.Throws<BridgeErrorException>(() => h.Run(args)).Error;
        Assert.Equal((ErrorReason.Timeout, ExecutionState.NotStarted), (failure.Reason, failure.Execution));
        Assert.Equal(2, batches);
        Assert.Equal(10_000, h.GuardReader.Milliseconds);
        Assert.Equal(1, h.GuardReader.NameReads);
        Assert.Equal(["+A1-K9", "+A1-K1"], h.GuardReader.GuardCandidates);
        Assert.Empty(h.Transactions.Calls);
        Assert.Equal(OutcomeStatus.NotStarted, h.Outcome());
    }
}

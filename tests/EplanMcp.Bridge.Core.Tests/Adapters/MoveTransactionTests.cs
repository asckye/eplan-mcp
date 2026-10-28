using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class MoveTransactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mismatch_BeforeOrAfterCommit_RestoresWithoutClosingUndo(bool committed)
    {
        FakeMoveNative.Calls.Clear();
        FakeMoveNative.Value = 0;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(new Factory(), "eplan_placement_update", () =>
        {
            FakeMoveNative.Value = 1;
            if (!committed)
            { throw Failed(); }
            return true;
        }, _ => true, _ => throw Failed(), () => Assert.Equal(0, FakeMoveNative.Value))).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(0, FakeMoveNative.Value);
        Assert.DoesNotContain("CloseUndo", FakeMoveNative.Calls);
        Assert.Contains(committed ? "DoUndo" : "Rollback", FakeMoveNative.Calls);
        Assert.Equal(1, FakeMoveNative.Calls.Count(c => c == "ReleaseSafety"));
        Assert.True(FakeMoveNative.Calls.IndexOf("Safety") < FakeMoveNative.Calls.IndexOf("Undo"));
    }

    [Fact]
    public void Success_VerifiesAfterCommitBeforeClosingTheOnlyUndo()
    {
        FakeMoveNative.Calls.Clear();
        TransactionRunner.Run(new Factory(), "eplan_placement_update", () => true, _ => true,
            _ => { Assert.Contains("Commit", FakeMoveNative.Calls); Assert.DoesNotContain("CloseUndo", FakeMoveNative.Calls); });
        Assert.Equal(1, FakeMoveNative.Calls.Count(c => c == "Undo"));
        Assert.Equal(1, FakeMoveNative.Calls.Count(c => c == "CloseUndo"));
        Assert.DoesNotContain("DoUndo", FakeMoveNative.Calls);
        Assert.DoesNotContain("Rollback", FakeMoveNative.Calls);
    }
    private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed, "Injected readback mismatch.");
    private sealed class Factory : ITransactionScopeFactory
    { public ITransactionScope Begin(string undoDescription) => new TransactionScope(undoDescription); }
}

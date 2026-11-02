using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class MeasuredRefusalTransactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasuredRefusal_RollsBackOnlyThisCall_ThenUndoRestoresPreviousStep(bool function)
    {
        FakeMoveNative.Calls.Clear();
        FakeMoveNative.History.Clear();
        FakeMoveNative.Value = 0;
        var factory = new Factory(function);
        TransactionRunner.Run(factory, "eplan_device_place", () => FakeMoveNative.Value = 1);
        Assert.Equal([0], FakeMoveNative.History);
        FakeMoveNative.Calls.Clear();

        var before = new LayoutGuard.Model { Page = new() { PageSize = [420, 297], GridSize = 4 } };
        var actual = new LayoutGuard.Model
        {
            Page = new()
            {
                PageSize = [420, 297],
                GridSize = 4,
                Placements = [new() { Kind = "PropertyPlacement", Location = [400, 160], Bounds = [400, 160, 424, 164] }]
            }
        };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(factory, "eplan_device_place", () =>
        {
            FakeMoveNative.Value = 2;
            LayoutGuard.AfterWriting(before, actual);
            return true;
        }, shouldCommit: _ => true)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("R1", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, FakeMoveNative.Value);
        Assert.Equal([0], FakeMoveNative.History);
        Assert.DoesNotContain("Commit", FakeMoveNative.Calls);
        Assert.DoesNotContain("DoUndo", FakeMoveNative.Calls);
        Assert.DoesNotContain("CloseUndo", FakeMoveNative.Calls);
        Assert.Contains(function ? "Abort" : "Rollback", FakeMoveNative.Calls);
        Assert.True(FakeMoveNative.Calls.IndexOf("ReleaseStep") < FakeMoveNative.Calls.IndexOf("ReleaseUndo"));
        FakeMoveNative.UndoPrevious();
        Assert.Equal(0, FakeMoveNative.Value);
        Assert.Empty(FakeMoveNative.History);
    }

    private sealed class Factory(bool function) : ITransactionScopeFactory
    {
        public ITransactionScope Begin(string undoDescription) => function
            ? new FunctionTransactionScope(new Project(), undoDescription) : new TransactionScope(undoDescription);
    }
}

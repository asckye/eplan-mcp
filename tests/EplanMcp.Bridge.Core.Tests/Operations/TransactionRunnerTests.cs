using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The order of the transaction calls, and what each failure means for the outcome (M3 design 4.2, ADR-0010): quality
/// scenario Q2 on the Bridge's side, with a fake project that a rollback restores.
/// </summary>
[Trait("Size", "Small")]
public sealed class TransactionRunnerTests
{
    private const string Tool = "eplan_page_create";

    private readonly FakeTransactions _transactions = new();
    private readonly List<string> _project = ["=A/1"];

    public TransactionRunnerTests()
    {
        _transactions.Snapshot = () =>
        {
            string[] before = [.. _project];
            return () =>
            {
                _project.Clear();
                _project.AddRange(before);
            };
        };
    }

    [Fact]
    public void AChangeThatPasses_IsCommittedThenItsUndoStepClosed()
    {
        Committed<int> committed = TransactionRunner.Run(_transactions, Tool, () => Change("=A/2"));

        Assert.Equal((2, null), (committed.Result, committed.Warning));
        Assert.Equal(["Begin AI: eplan_page_create", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal(["=A/1", "=A/2"], _project);
    }

    [Fact]
    public void AChangeThatThrowsHalfway_IsRolledBackAndNeverCommitted()
    {
        var failure = new InvalidOperationException("EPLAN failed on the second page");

        Exception thrown = Assert.Throws<InvalidOperationException>(() => TransactionRunner.Run<int>(_transactions, Tool, () =>
        {
            Change("=A/2");
            throw failure;
        }));

        Assert.Same(failure, thrown);
        Assert.Equal(["Begin AI: eplan_page_create", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
        Assert.Equal(["=A/1"], _project);
    }

    [Fact]
    public void AFailedReadBack_IsRolledBackAndSaysSo()
    {
        var mismatch = new BridgeErrorException(ErrorReason.InvalidArgument, "read back differs");
        mismatch.Error.Execution = ExecutionState.NotStarted; // whatever the change claimed, it ran

        BridgeError error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run<int>(_transactions, Tool, () =>
        {
            Change("=A/2");
            throw mismatch;
        })).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.DoesNotContain("Commit", _transactions.Calls);
        Assert.Equal(["=A/1"], _project);
    }

    [Theory]
    [InlineData("Rollback")]
    [InlineData("Dispose")]
    public void ARollbackThatFails_MakesTheOutcomeUnknown(string step)
    {
        _transactions.FailAt = step;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run<int>(_transactions, Tool, () => throw new InvalidOperationException("x")));

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown, false), (error.Error.Reason, error.Error.Execution, error.Error.Retryable));
        Assert.Contains("'AI: eplan_page_create'", error.Error.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Fact]
    public void ACommitThatFails_MakesTheOutcomeUnknownAndIsNeverRolledBack()
    {
        _transactions.FailAt = "Commit";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(_transactions, Tool, () => Change("=A/2"))).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Equal(["Begin AI: eplan_page_create", "Commit", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void AnUndoStepThatDoesNotClose_KeepsTheCommitAndWarns()
    {
        _transactions.FailAt = "CloseUndo";

        Committed<int> committed = TransactionRunner.Run(_transactions, Tool, () => Change("=A/2"));

        Assert.Equal(2, committed.Result);
        Assert.Contains("Ctrl+Z", committed.Warning, StringComparison.Ordinal);
        Assert.Equal(["=A/1", "=A/2"], _project);
    }

    [Fact]
    public void AnUndoStepThatDoesNotCloseAfterAFailure_StillRollsBack()
    {
        _transactions.FailAt = "CloseUndo";

        Assert.Throws<InvalidOperationException>(() => TransactionRunner.Run<int>(_transactions, Tool, () =>
        {
            Change("=A/2");
            throw new InvalidOperationException("x");
        }));

        Assert.Contains("Rollback", _transactions.Calls);
        Assert.Equal(["=A/1"], _project);
    }

    [Fact]
    public void ADisposeThatFailsAfterTheCommit_IsAWarning()
    {
        _transactions.FailAt = "Dispose";

        Committed<int> committed = TransactionRunner.Run(_transactions, Tool, () => Change("=A/2"));

        Assert.NotNull(committed.Warning);
    }

    [Fact]
    public void ThroughTheRouter_AnUnknownOutcomeIsRecordedAndItsCauseLogged()
    {
        var log = new ListLog();
        _transactions.FailAt = "Rollback";
        var router = new OperationRouter([new Write(_transactions)], new InlineDispatcher(), null, log, TimeSpan.FromSeconds(10));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke("page.create.execute", null, "r1")).Error;

        Assert.Equal(ExecutionState.Unknown, error.Execution);
        Assert.Equal(OutcomeStatus.Unknown, router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = "r1" }, "c")["status"]!.ToString());
        Assert.Contains(log.Entries, e => e.Exception is InvalidOperationException { Message: "Rollback failed" });
    }

    private int Change(string page)
    {
        _project.Add(page);
        return _project.Count;
    }

    private sealed class Write(FakeTransactions transactions) : IWriteOperation
    {
        public string Name => "page.create.execute";

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args) => TransactionRunner.Run<int>(transactions, Tool, () => throw new InvalidOperationException("x"));
    }
}

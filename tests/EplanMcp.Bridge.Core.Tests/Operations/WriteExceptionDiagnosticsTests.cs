using Eplan.EplApi.Base;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class WriteExceptionDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFailure_LogsWarningAndCorrelation_EvenIfRollbackFails(bool rollbackFails)
    {
        var log = new ListLog();
        var transactions = new FakeTransactions { FailAt = rollbackFails ? "Rollback" : null };
        var native = new BaseException();
        var wrapped = new BridgeErrorException(ErrorReason.EplanOperationFailed, "Failed", native);
        new OperationContext("db10bb36", true, null, log).Run(() =>
        {
            BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run<int>(transactions, "trial", () => throw wrapped));
            Assert.Equal(rollbackFails ? ExecutionState.Unknown : ExecutionState.RolledBack, error.Error.Execution);
            return null;
        });
        var warning = Assert.Single(log.Entries);
        Assert.Equal(BridgeLogLevel.Warning, warning.Level);
        Assert.Equal("[db10bb36] native exception inside a write; EPLAN state may be unstable", warning.Message);
        Assert.Null(warning.Exception);
        Assert.DoesNotContain("Commit", transactions.Calls);
    }

    [Fact]
    public void SwallowedNativeException_IsLoggedOnce_AndDoesNotChangeCommit()
    {
        var log = new ListLog();
        var transactions = new FakeTransactions();
        new OperationContext("699c6dd4", true, null, log).Run(() => TransactionRunner.Run(transactions, "trial", () =>
        {
#pragma warning disable CA2201 // Preserve the native NameService exception type from the reported failure.
            try
            { throw new ApplicationException("S029128未确定任何页"); }
#pragma warning restore CA2201
            catch (ApplicationException ex)
            {
                WriteExceptionDiagnostics.Report(ex);
                WriteExceptionDiagnostics.Report(new InvalidOperationException("wrapped", ex));
            }
            return 1;
        }));
        Assert.Single(log.Entries);
        Assert.Equal(["Begin AI: trial", "Commit", "CloseUndo", "Dispose"], transactions.Calls);
    }

    [Fact]
    public void NativeExceptionOutsideTransaction_AndManagedFailureInside_DoNotWarn()
    {
        var log = new ListLog();
        var context = new OperationContext("read", true, null, log);
        context.Run(() =>
        {
            WriteExceptionDiagnostics.Report(new BaseException());
            Assert.Throws<InvalidOperationException>(() => TransactionRunner.Run<int>(new FakeTransactions(), "trial", () => throw new InvalidOperationException("managed")));
            WriteExceptionDiagnostics.Report(new BaseException());
            return null;
        });
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("Commit")]
    [InlineData("CloseUndo")]
    [InlineData("Dispose")]
    public void NativeScopeFailure_LogsAndPreservesExistingOutcome(string step)
    {
        var log = new ListLog();
#pragma warning disable CA2000 // TransactionRunner owns the scope, including its failing Dispose path.
        var factory = new NativeTransactions(step);
#pragma warning restore CA2000
        new OperationContext("scope", true, null, log).Run(() =>
        {
            if (step == "Commit")
            { Assert.Equal(ExecutionState.Unknown, Assert.Throws<BridgeErrorException>(() => TransactionRunner.Run(factory, "trial", () => 1)).Error.Execution); }
            else
            { Assert.NotNull(TransactionRunner.Run(factory, "trial", () => 1).Warning); }
            return null;
        });
        Assert.Single(log.Entries);
    }

    private sealed class NativeTransactions(string failAt) : ITransactionScopeFactory, ITransactionScope
    {
        private readonly BaseException _failure = new();
        public ITransactionScope Begin(string undoDescription) => this;
        public void Commit() => Step("Commit");
        public void CloseUndo() => Step("CloseUndo");
        public void Rollback() => Step("Rollback");
        public void Dispose() => Step("Dispose");
        private void Step(string step) { if (step == failAt) { throw _failure; } }
    }
}

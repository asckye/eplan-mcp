using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// operation.cancel and the safe points (M4 design 4.3): a cancel stops an invoke only between steps, and what it
/// leaves behind is the execution state ADR-0010 names for that stage. The cancels here come from the operation's own
/// thread, standing for a pipe thread that sends one at that moment.
/// </summary>
[Trait("Size", "Small")]
public sealed class CancellationTests
{
    private const string Tool = "eplan_page_create";
    private const string WriteName = "page.create.execute";

    private readonly ListLog _log = new();
    private readonly FakeTransactions _transactions = new();

    [Fact]
    public void CancelledWhileWaitingForTheMainThread_AWriteNeverStarts()
    {
        int runs = 0;
        OperationRouter router = null!;
        router = Router(new CancelFirstDispatcher(() => Cancel(router, "r1")), new Write(() => runs++));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(WriteName, null, "r1")).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Equal(0, runs);
        Assert.Equal((OutcomeStatus.NotStarted, ErrorReason.Cancelled), Outcome(router, "r1"));
    }

    [Fact]
    public void CancelledDuringTheChange_AWriteIsRolledBackInsteadOfCommitted()
    {
        OperationRouter router = null!;
        router = Router(new InlineDispatcher(), new Write(() => TransactionRunner.Run(_transactions, Tool, () => Cancel(router, "r1"))));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(WriteName, null, "r1")).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(["Begin AI: eplan_page_create", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
        Assert.Equal((OutcomeStatus.RolledBack, ErrorReason.Cancelled), Outcome(router, "r1"));
    }

    [Fact]
    public void CancelledAfterTheCommit_AWriteStaysCommitted()
    {
        OperationRouter router = null!;
        router = Router(new InlineDispatcher(), new Write(() =>
        {
            bool committed = TransactionRunner.Run(_transactions, Tool, () => true).Result;
            return Cancel(router, "r1") && committed;
        }));

        JToken data = router.Invoke(WriteName, null, "r1");

        Assert.True((bool)data);
        Assert.Equal(["Begin AI: eplan_page_create", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal((OutcomeStatus.Committed, (string?)null), Outcome(router, "r1"));
    }

    [Fact]
    public void AnOperation_StopsAtItsNextSafePointNotInTheMiddleOfAStep()
    {
        var done = new List<int>();
        OperationRouter router = null!;
        router = Router(new InlineDispatcher(), new DelegateOperation("x.pages", () =>
        {
            for (int page = 1; page <= 5; page++)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if (page == 2)
                {
                    Cancel(router, "c1"); // arrives while page 2 is being exported
                }

                done.Add(page);
            }

            return null;
        }));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke("x.pages", null, "c1")).Error;

        Assert.Equal([1, 2], done);
        Assert.Equal((ErrorReason.Cancelled, null), (error.Reason, error.Execution)); // a read carries no execution state
        Assert.Contains(_log.Entries, e => e.Message.Contains("x.pages cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public void Cancel_FindsOnlyTheInvokeWithThatRequestIdWhileItRuns()
    {
        var found = new List<bool>();
        OperationRouter router = null!;
        router = Router(new InlineDispatcher(), new DelegateOperation("x.read", () =>
        {
            found.Add(Cancel(router, "other"));
            bool flaggedByTheOther = OperationContext.Current.IsCancellationRequested;
            found.Add(Cancel(router, "c1"));
            return flaggedByTheOther; // no safe point follows, so the read still answers
        }));

        JToken answer = router.Invoke("x.read", null, "c1");

        Assert.False((bool)answer);
        Assert.Equal([false, true], found);
        Assert.False(Cancel(router, "c1")); // answered: nothing left to cancel
    }

    [Fact]
    public void Cancel_IsAnsweredWithoutEplansMainThread()
    {
        // A dispatcher that refuses everything: operation.cancel never goes through it.
        OperationRouter router = Router(new BusyDispatcher(BusyReason.MainThreadBusy));

        Assert.False(Cancel(router, "never-sent"));
        Assert.Contains(OperationNames.OperationCancel, router.SupportedOperations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"requestId":""}""")]
    [InlineData("""{"requestId":7}""")]
    public void Cancel_WithoutARequestId_IsInvalid(string? args)
    {
        OperationRouter router = Router(new InlineDispatcher());

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.OperationCancel, args is null ? null : JToken.Parse(args), "c")).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
    }

    [Fact]
    public void OutsideAnInvoke_TheContextIsNeverCancelled()
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        OperationContext.Current.ReportProgress(1, 2, "dropped");

        Assert.False(OperationContext.Current.IsCancellationRequested);
        Assert.Equal(1, TransactionRunner.Run(_transactions, Tool, () => 1).Result);
    }

    private OperationRouter Router(IMainThreadDispatcher dispatcher, params IOperation[] operations) => new(operations, dispatcher, null, _log, TimeSpan.FromSeconds(10));

    private static bool Cancel(OperationRouter router, string requestId) =>
        (bool)router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = requestId }, "cancel")["found"]!;

    private static (string Status, string? Reason) Outcome(OperationRouter router, string requestId)
    {
        JToken outcome = router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = requestId }, "c");
        return ((string)outcome["status"]!, (string?)outcome["reason"]);
    }

    /// <summary>Runs <paramref name="before"/>, standing for a cancel that arrives while the work waits, then the work.</summary>
    private sealed class CancelFirstDispatcher(Action before) : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            before();
            return new InlineDispatcher().Run(work, startTimeout);
        }
    }

    private sealed class Write(Func<object?> execute) : IWriteOperation
    {
        public string Name => WriteName;

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args) => execute();
    }
}

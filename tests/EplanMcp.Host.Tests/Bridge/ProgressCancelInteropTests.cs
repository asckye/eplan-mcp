using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// Progress and cancellation between the Host's BridgeClient and Bridge.Core's server on a real named pipe (M4 design
/// 4.3, 7): the Bridge's progress reaches the caller, and a caller's cancel goes over a new connection and stops a
/// running invoke at its next safe point; a write's outcome is then what the Bridge recorded. EPLAN's main thread is
/// the server's own thread, and its transactions are fakes.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProgressCancelInteropTests : IAsyncDisposable
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);
    private static readonly string[] _stepMessages = ["Step 1 of 3", "Step 2 of 3", "Step 3 of 3"];

    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly RecordedReports<ProgressNotification> _reports = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _transactions = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public ProgressCancelInteropTests()
    {
        IOperation[] operations =
        [
            new Read("test.steps", Steps),
            new Read("test.wait", WaitForTheCancel),
            new Write("test.write", () => TransactionRunner.Run(new Transactions(_transactions), "test_write", WaitForTheCancel).Result),
            new Write("test.commit", () => CommitThenWait()),
            new Write("test.report", () => TransactionRunner.Run(new Transactions(_transactions), "test_write", Steps).Result),
        ];
        var router = new OperationRouter(operations, new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions
            {
                BridgeVersion = "0.1.0-test",
                Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" },
                ProgressInterval = TimeSpan.FromMilliseconds(10),
            },
            router,
            new NullLog());
        _server.Start();
        _client = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(20));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task TheProgressOfARead_ReachesTheCaller()
    {
        await _client.InvokeAsync<object, JsonObject>("test.steps", new { }, _patience, _reports, Token);

        AssertSteps();
    }

    [Fact]
    public async Task TheProgressOfAWrite_ReachesTheCaller()
    {
        WriteResult<JsonObject> result = await _client.InvokeWriteAsync<object, JsonObject>("test.report", new { }, "w0", _patience, _reports, Token);

        Assert.Equal((3, false), ((int)result.Data!["steps"]!, result.Recovered));
        AssertSteps("w0");
    }

    [Fact]
    public async Task ACancelledRead_StopsAtTheBridgesNextSafePoint()
    {
        using var cancel = new CancellationTokenSource();
        Task call = _client.InvokeAsync<object, JsonObject>("test.wait", new { }, cancel.Token);
        await _started.Task.WaitAsync(_patience, Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(await _stopped.Task.WaitAsync(_patience, Token), "the Bridge never saw the cancel");
    }

    [Fact]
    public async Task AWriteCancelledBeforeItsCommit_IsRolledBack()
    {
        using var cancel = new CancellationTokenSource();
        Task<WriteResult<JsonObject>> call = _client.InvokeWriteAsync<object, JsonObject>("test.write", new { }, "w1", _patience, null, cancel.Token);
        await _started.Task.WaitAsync(_patience, Token);

        await cancel.CancelAsync();

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => call)).Error;
        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Equal(["Begin", "CloseUndo", "Rollback", "Dispose"], _transactions.ToArray());
    }

    [Fact]
    public async Task AWriteCancelledAfterItsCommit_StaysCommitted()
    {
        using var cancel = new CancellationTokenSource();
        Task<WriteResult<JsonObject>> call = _client.InvokeWriteAsync<object, JsonObject>("test.commit", new { }, "w2", _patience, null, cancel.Token);
        await _started.Task.WaitAsync(_patience, Token);

        await cancel.CancelAsync();

        Assert.True((await call).Recovered);
        Assert.True(await _stopped.Task.WaitAsync(_patience, Token), "the Bridge never saw the cancel");
        Assert.Equal(["Begin", "Commit", "CloseUndo", "Dispose"], _transactions.ToArray());
    }

    private void AssertSteps(string? requestId = null)
    {
        List<ProgressNotification> reports = _reports.Values;
        Assert.Equal([1, 2, 3], reports.Select(r => r.Progress));
        Assert.All(reports, r => Assert.Equal(3, r.Total));
        Assert.Equal(_stepMessages, reports.Select(r => r.Message));
        string id = Assert.Single(reports.Select(r => r.RequestId).Distinct(StringComparer.Ordinal));
        Assert.Equal(requestId ?? id, id);
        Assert.NotEmpty(id);
    }

    /// <summary>Three steps, each reported once it is done.</summary>
    private object? Steps()
    {
        for (int step = 1; step <= 3; step++)
        {
            OperationContext.Current.ReportProgress(step, 3, _stepMessages[step - 1]);

            // The Bridge keeps only the newest report: wait until this one went out, as the test is about forwarding.
            int sent = step;
            SpinWait.SpinUntil(() => _reports.Values.Count >= sent, _patience);
        }

        return new { steps = 3 };
    }

    /// <summary>Works until the Host cancels, then stops at the safe point after the step, as an export does.</summary>
    private object? WaitForTheCancel()
    {
        OperationContext context = OperationContext.Current;
        _started.TrySetResult();
        _stopped.TrySetResult(SpinWait.SpinUntil(() => context.IsCancellationRequested, _patience));
        context.ThrowIfCancellationRequested();
        return new { };
    }

    /// <summary>Commits at once, then works on without a safe point: a cancel after the commit changes nothing.</summary>
    private bool CommitThenWait()
    {
        bool committed = TransactionRunner.Run(new Transactions(_transactions), "test_write", () => true).Result;
        OperationContext context = OperationContext.Current;
        _started.TrySetResult();
        _stopped.TrySetResult(SpinWait.SpinUntil(() => context.IsCancellationRequested, _patience));
        return committed;
    }

    private sealed class Read(string name, Func<object?> run) : IOperation
    {
        public string Name => name;

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args) => run();
    }

    private sealed class Write(string name, Func<object?> run) : IWriteOperation
    {
        public string Name => name;

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args) => run();
    }

    private sealed class Transactions(ConcurrentQueue<string> calls) : ITransactionScopeFactory
    {
        public ITransactionScope Begin(string undoDescription)
        {
            calls.Enqueue("Begin");
            return new Scope(calls);
        }

        private sealed class Scope(ConcurrentQueue<string> calls) : ITransactionScope
        {
            public void Commit() => calls.Enqueue("Commit");

            public void CloseUndo() => calls.Enqueue("CloseUndo");

            public void Rollback() => calls.Enqueue("Rollback");

            public void Dispose() => calls.Enqueue("Dispose");
        }
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}

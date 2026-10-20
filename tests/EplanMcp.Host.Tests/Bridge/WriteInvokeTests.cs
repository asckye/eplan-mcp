using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// The Host's half of ADR-0010 section 2 against a scripted Bridge: a write is sent once, and after an interruption its
/// outcome is fetched from the same Bridge, never guessed (SEC-18, Q11).
/// </summary>
[Trait("Size", "Small")]
public sealed class WriteInvokeTests
{
    private const string Execute = "page.create.execute";
    private const string RequestId = "r1";

    private readonly FakeDiscovery _discovery = new();
    private readonly BridgeEndpoint _bridge = new("EplanMcp.Bridge.42.a1", 42);
    private readonly Queue<OutcomeGetData> _outcomes = new();
    private readonly Queue<bool> _found = new([true]);
    private Func<CancellationToken, Task<JsonNode?>> _execute = _ => Task.FromResult<JsonNode?>(new JsonObject { ["data"] = new JsonObject { ["pages"] = 2 } });
    private List<string> _operations = [Execute, OperationNames.OutcomeGet, OperationNames.OperationCancel];

    public WriteInvokeTests() => _discovery.Endpoints.Add(_bridge);

    private int Executes { get; set; }

    private int OutcomeQuestions { get; set; }

    private int Cancels { get; set; }

    [Fact]
    public async Task AWrite_ThatAnswers_IsCommittedAndSentOnceUnderTheRequestId()
    {
        WriteResult<JsonObject> result = await Write();

        Assert.Equal((2, false), ((int)result.Data!["pages"]!, result.Recovered));
        Assert.Equal((1, 0), (Executes, OutcomeQuestions));
    }

    [Fact]
    public async Task AWrite_ToABridgeWithoutOutcomeGet_IsNeverSent()
    {
        _operations = [Execute];

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.BridgeIncompatible, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, Executes);
    }

    [Theory]
    [InlineData(ExecutionState.RolledBack, ExecutionState.RolledBack)]
    [InlineData(ExecutionState.NotStarted, ExecutionState.NotStarted)]
    [InlineData(null, ExecutionState.NotStarted)]
    public async Task AWrite_TheBridgeRefuses_KeepsTheBridgesState(string? stamped, string expected)
    {
        _execute = _ => throw new BridgeRpcException(
            BridgeProtocol.BusinessErrorCode,
            "m",
            JsonSerializer.SerializeToNode(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "m", Execution = stamped }, HostJson.Options));

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.EplanOperationFailed, expected), (error.Reason, error.Execution));
        Assert.Equal(0, OutcomeQuestions);
    }

    [Fact]
    public async Task AWrite_WhoseResponseIsLate_IsFetchedUntilItIsCommitted()
    {
        _execute = Never;
        _outcomes.Enqueue(Outcome(OutcomeStatus.Running));
        _outcomes.Enqueue(Outcome(OutcomeStatus.Running));
        _outcomes.Enqueue(Outcome(OutcomeStatus.Committed));

        WriteResult<JsonObject> result = await Write();

        Assert.Equal((null, true), (result.Data, result.Recovered));
        Assert.Equal((1, 3), (Executes, OutcomeQuestions));
    }

    [Fact]
    public async Task AWrite_WhoseConnectionDrops_ReportsTheRecordedRollback()
    {
        _execute = Lost;
        _outcomes.Enqueue(Outcome(OutcomeStatus.RolledBack, ErrorReason.LockFailed));

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.LockFailed, ExecutionState.RolledBack, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Equal(1, Executes);
    }

    [Fact]
    public async Task AWrite_ThatNeverArrived_IsTheInterruptionAndMayBeRetried()
    {
        _execute = Lost;
        _outcomes.Enqueue(new OutcomeGetData { Status = OutcomeStatus.NotStarted });

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.EplanNotConnected, ExecutionState.NotStarted, true), (error.Reason, error.Execution, error.Retryable));
    }

    [Fact]
    public async Task AWrite_StillRunningAtItsLimit_IsUnknown()
    {
        _execute = Never;
        _outcomes.Enqueue(Outcome(OutcomeStatus.Running));

        // The response wait alone takes 100 ms; 300 ms left room for one poll only on a busy CI runner (2026-10-20).
        BridgeError error = await Fails(limit: TimeSpan.FromSeconds(2));

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown, false), (error.Reason, error.Execution, error.Retryable));
        Assert.True(OutcomeQuestions > 1);
        Assert.Equal(1, Executes);
    }

    [Theory]
    [InlineData(OutcomeStatus.Unknown)]
    [InlineData(OutcomeStatus.Expired)]
    public async Task AWrite_TheBridgeCannotVouchFor_IsUnknown(string status)
    {
        _execute = Lost;
        _outcomes.Enqueue(Outcome(status));

        Assert.Equal(ExecutionState.Unknown, (await Fails()).Execution);
    }

    [Fact]
    public async Task AWrite_WhoseEplanIsGone_IsUnknownWithoutWaiting()
    {
        _execute = token =>
        {
            _discovery.Endpoints.Clear();
            throw new IOException("pipe broken");
        };

        BridgeError error = await Fails(limit: TimeSpan.FromMinutes(5));

        Assert.Equal(ExecutionState.Unknown, error.Execution);
        Assert.Equal(0, OutcomeQuestions);
    }

    [Fact]
    public async Task AWrite_WhoseEplanRestarted_IsUnknown()
    {
        // Same process ID, new pipe name: another Bridge, which knows nothing of the write.
        _execute = token =>
        {
            _discovery.Endpoints[0] = new BridgeEndpoint("EplanMcp.Bridge.42.b2", 42);
            throw new IOException("pipe broken");
        };
        _outcomes.Enqueue(new OutcomeGetData { Status = OutcomeStatus.NotStarted });

        Assert.Equal(ExecutionState.Unknown, (await Fails()).Execution);
        Assert.Equal(0, OutcomeQuestions);
    }

    [Fact]
    public async Task AWrite_CommittedWithUnreadableData_IsAnInternalErrorAfterTheCommit()
    {
        _execute = _ => Task.FromResult<JsonNode?>(new JsonObject { ["data"] = "not an object" });

        BridgeError error = await Fails();

        Assert.Equal((ErrorReason.Internal, ExecutionState.Committed), (error.Reason, error.Execution));
    }

    [Fact]
    public async Task AWrite_TheCallerCancels_IsStoppedInTheBridgeAndItsRollbackReported()
    {
        using var cancel = new CancellationTokenSource();
        _execute = token => CancelThenWait(cancel, token);
        _outcomes.Enqueue(Outcome(OutcomeStatus.RolledBack, ErrorReason.Cancelled));

        BridgeError error = await Fails(token: cancel.Token);

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Equal((1, 1), (Executes, Cancels));
    }

    [Fact]
    public async Task AWrite_CancelledBeforeTheBridgeReadIt_NeverRuns()
    {
        using var cancel = new CancellationTokenSource();
        _execute = token => CancelThenWait(cancel, token);
        _found.Clear();
        _found.Enqueue(false);
        _outcomes.Enqueue(new OutcomeGetData { Status = OutcomeStatus.NotStarted });

        BridgeError error = await Fails(token: cancel.Token);

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
    }

    [Fact]
    public async Task AWrite_TheBridgeReadsOnlyAfterTheCancel_IsAskedAgainAndWhatItDidReported()
    {
        using var cancel = new CancellationTokenSource();
        _execute = token => CancelThenWait(cancel, token);
        _found.Clear();
        _found.Enqueue(false);
        _found.Enqueue(true);
        _outcomes.Enqueue(Outcome(OutcomeStatus.Running));
        _outcomes.Enqueue(Outcome(OutcomeStatus.Committed)); // the cancel came after its last safe point

        WriteResult<JsonObject> result = await Write(token: cancel.Token);

        Assert.True(result.Recovered);
        Assert.Equal((1, 2), (Executes, Cancels));
    }

    [Fact]
    public async Task AWrite_CancelledOnAnAddInThatCannotCancel_IsWaitedFor()
    {
        using var cancel = new CancellationTokenSource();
        _operations = [Execute, OperationNames.OutcomeGet];
        _execute = token => CancelThenWait(cancel, token);
        _outcomes.Enqueue(Outcome(OutcomeStatus.Running));
        _outcomes.Enqueue(Outcome(OutcomeStatus.Committed));

        WriteResult<JsonObject> result = await Write(token: cancel.Token);

        Assert.True(result.Recovered);
        Assert.Equal(0, Cancels);
    }

    [Fact]
    public async Task AWrite_CancelledWhileQueued_IsNeverSent()
    {
        var release = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _operations = [Execute, OperationNames.OutcomeGet, "x.read"];
        var connector = new FakeConnector(endpoint => new FakeChannel(endpoint, (method, parameters, token) =>
            parameters!["operation"]!.GetValue<string>() == "x.read" ? release.Task : Answer(method, parameters, token))
        {
            Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(endpoint.ProcessId, operations: _operations)),
        });
        await using var client = new BridgeClient(_discovery, connector, NullLogger<BridgeClient>.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));
        using var cancel = new CancellationTokenSource();

        Task<JsonObject> read = client.InvokeAsync<object, JsonObject>("x.read", new { }, TestContext.Current.CancellationToken);
        Task<WriteResult<JsonObject>> write = client.InvokeWriteAsync<object, JsonObject>(Execute, new { }, RequestId, TimeSpan.FromSeconds(5), null, cancel.Token);
        await cancel.CancelAsync();
        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => write)).Error;
        release.SetResult(new JsonObject { ["data"] = new JsonObject() });
        await read;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal((0, 0), (Executes, Cancels));
    }

    [Theory]
    [InlineData(ErrorReason.EplanBusy, true, ExecutionState.NotStarted, true)]
    [InlineData(ErrorReason.LockFailed, true, ExecutionState.RolledBack, true)]
    [InlineData(ErrorReason.Internal, false, ExecutionState.Committed, true)]
    [InlineData(ErrorReason.OutcomeUnknown, false, ExecutionState.Unknown, true)]
    [InlineData(ErrorReason.EplanBusy, true, ExecutionState.Committed, false)]
    [InlineData(ErrorReason.Timeout, true, ExecutionState.Unknown, false)]
    [InlineData(ErrorReason.EplanOperationFailed, false, ExecutionState.Unknown, false)]
    [InlineData(ErrorReason.OutcomeUnknown, false, ExecutionState.RolledBack, false)]
    [InlineData(ErrorReason.Internal, false, null, false)]
    [InlineData(ErrorReason.Internal, false, "maybe", false)]
    public void Invariants_FollowAdr0010(string reason, bool retryable, string? execution, bool hold)
    {
        Assert.Equal(hold, ExecutionInvariants.Hold(new BridgeError { Reason = reason, Retryable = retryable, Execution = execution }));
    }

    private async Task<WriteResult<JsonObject>> Write(TimeSpan? limit = null, CancellationToken? token = null)
    {
        var connector = new FakeConnector(endpoint => new FakeChannel(endpoint, Answer)
        {
            Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(endpoint.ProcessId, operations: _operations)),
        });
        await using var client = new BridgeClient(_discovery, connector, NullLogger<BridgeClient>.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10));
        return await client.InvokeWriteAsync<object, JsonObject>(Execute, new { }, RequestId, limit ?? TimeSpan.FromSeconds(5), null, token ?? TestContext.Current.CancellationToken);
    }

    private async Task<BridgeError> Fails(TimeSpan? limit = null, CancellationToken? token = null)
    {
        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Write(limit, token))).Error;
        Assert.True(ExecutionInvariants.Hold(error), $"{error.Reason}/{error.Execution}/{error.Retryable}");
        Assert.True(Executes <= 1, "a write was sent twice");
        return error;
    }

    private Task<JsonNode?> Answer(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        string operation = parameters!["operation"]!.GetValue<string>();
        if (operation == OperationNames.OutcomeGet)
        {
            OutcomeQuestions++;
            Assert.Equal(RequestId, parameters["args"]!["requestId"]!.GetValue<string>());
            OutcomeGetData next = _outcomes.Count > 1 ? _outcomes.Dequeue() : _outcomes.Peek();
            return Task.FromResult<JsonNode?>(new JsonObject { ["data"] = JsonSerializer.SerializeToNode(next, HostJson.Options) });
        }

        if (operation == OperationNames.OperationCancel)
        {
            Cancels++;
            Assert.Equal(RequestId, parameters["args"]!["requestId"]!.GetValue<string>());
            bool found = _found.Count > 1 ? _found.Dequeue() : _found.Peek();
            return Task.FromResult<JsonNode?>(new JsonObject { ["data"] = new JsonObject { ["found"] = found } });
        }

        Executes++;
        Assert.Equal(RequestId, parameters["correlationId"]!.GetValue<string>());
        return _execute(cancellationToken);
    }

    private static OutcomeGetData Outcome(string status, string? reason = null) => new() { Status = status, Operation = Execute, Reason = reason };

    private static async Task<JsonNode?> Never(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return null;
    }

    private static Task<JsonNode?> Lost(CancellationToken cancellationToken) => throw new IOException("pipe broken");

    /// <summary>The client cancels the tool call while the write is on its way to EPLAN.</summary>
    private static async Task<JsonNode?> CancelThenWait(CancellationTokenSource cancel, CancellationToken cancellationToken)
    {
        await cancel.CancelAsync();
        return await Never(cancellationToken);
    }
}

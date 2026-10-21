using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// The real Host channel (WorkbenchChannel) against the real Workbench pipe server, headless (ADR-0014, SEC-22, SEC-24,
/// M5 design 7), on a pipe of this test's own, both sides expecting this test process as the other: each way a question
/// ends, a full queue, several Hosts at once, a refused client, a name already taken, the pipe's ACL, and what a broken
/// or hostile client gets.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ApprovalPipeTests : IAsyncDisposable
{
    /// <summary>How long a step may take before the test fails instead of hanging.</summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The owner the test servers give their pipes: the token's, which the Host's CurrentUserOnly compares with. It is
    /// the user, unless the tests run elevated, as on CI; the real owner is checked on its own.
    /// </summary>
    private static readonly SecurityIdentifier _tokenOwner = TokenOwner();

    private readonly ManualTime _clock = new(Asks.Now);
    private readonly string _pipe = WorkbenchPipeName.For("S-1-5-21-0-0-0-1000", Path.Combine(Path.GetTempPath(), "eplanmcp-tests", Guid.NewGuid().ToString("N")));
    private readonly ApprovalQueue _queue;
    private readonly ApprovalServer _server;

    public ApprovalPipeTests()
    {
        _queue = new ApprovalQueue(_clock);
        _server = Server(_queue);
        Assert.True(_server.Start());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task AnApprovedWrite_ReachesTheHostWithItsBinding_AndItsOutcomeIsShown()
    {
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> asking = Channel().AskAsync(HostQuestion(), Token);
        Question question = await arrival.WaitAsync(_patience, Token);

        Assert.Equal(("r1", "eplan_page_create", QuestionTier.Write, "claude-code"), (question.RequestId, question.Ask.Tool, question.Ask.Tier, question.Ask.ClientName));
        Assert.True(_queue.Answer(question, approve: true));
        ApprovalAnswer answer = (await asking.WaitAsync(_patience, Token))!;
        Assert.Equal(new ApprovalAnswer("r1", "f1", "h1", ApprovalDecision.Granted), answer with { Outcome = null });

        Task shown = Changed(question, q => q.Execution != QuestionExecution.Running);
        await answer.Outcome!.ReportAsync(ExecutionState.Committed, null);
        answer.Outcome.Dispose();
        await shown.WaitAsync(_patience, Token);
        Assert.Equal((QuestionEnd.Approved, QuestionExecution.Committed), (question.End, question.Execution));
    }

    [Fact]
    public async Task ARejection_IsDenied_AndAnApprovedWriteThatReportsNothing_SaysNoResultCame()
    {
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> rejected = Channel().AskAsync(HostQuestion("r1"), Token);
        _queue.Answer(await arrival.WaitAsync(_patience, Token), approve: false);
        Assert.Equal(ApprovalDecision.Denied, (await rejected.WaitAsync(_patience, Token))!.Decision);

        arrival = Arrival();
        Task<ApprovalAnswer?> approved = Channel().AskAsync(HostQuestion("r2"), Token);
        Question question = await arrival.WaitAsync(_patience, Token);
        Task closed = Changed(question, q => q.Execution == QuestionExecution.NotReceived);
        _queue.Answer(question, approve: true);
        (await approved.WaitAsync(_patience, Token))!.Outcome!.Dispose();

        await closed.WaitAsync(_patience, Token);
    }

    [Fact]
    public async Task NoAnswerByTheDeadline_HasExpiredOnBothSides()
    {
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> asking = Channel().AskAsync(HostQuestion(), Token);
        Question question = await arrival.WaitAsync(_patience, Token);

        _clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Null(question.End);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ApprovalDecision.Expired, (await asking.WaitAsync(_patience, Token))!.Decision);
        Assert.Equal(QuestionEnd.Expired, question.End);
        Assert.False(_queue.Answer(question, approve: true));
    }

    [Fact]
    public async Task TheClientCancellingTheCall_TakesTheQuestionAway()
    {
        using var client = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> asking = Channel().AskAsync(HostQuestion(), client.Token);
        Question question = await arrival.WaitAsync(_patience, Token);
        Task ended = Changed(question, q => q.End is not null);

        await client.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
        await ended.WaitAsync(_patience, Token);
        Assert.Equal(QuestionEnd.Cancelled, question.End);
        Assert.Empty(_queue.Pending);
    }

    [Fact]
    public async Task AHostThatHangsUp_LeavesItsQuestionDisconnected_AndOthersWaiting()
    {
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> other = Channel().AskAsync(HostQuestion("r1"), Token);
        Question waiting = await arrival.WaitAsync(_patience, Token);
        arrival = Arrival();
        Question gone;
        await using (NamedPipeClientStream pipe = await ConnectAsync())
        {
            await HandshakeAsync(pipe);
            await SendAsync(pipe, WorkbenchJson.Request(2, WorkbenchProtocol.ApprovalAskMethod, Asks.Write("r2")));
            gone = await arrival.WaitAsync(_patience, Token);
        }

        await Changed(gone, q => q.End is not null).WaitAsync(_patience, Token);
        Assert.Equal(QuestionEnd.Disconnected, gone.End);
        Assert.Equal([waiting], _queue.Pending);
        _queue.Answer(waiting, approve: false);
        Assert.Equal(ApprovalDecision.Denied, (await other.WaitAsync(_patience, Token))!.Decision);
    }

    [Fact]
    public async Task SeveralHostsAtOnce_EachGetTheAnswerToTheirOwnQuestion()
    {
        var arrived = new List<Question>();
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Changed += q =>
        {
            lock (arrived)
            {
                if (q.End is null && !arrived.Contains(q))
                {
                    arrived.Add(q);
                }

                if (arrived.Count == 2)
                {
                    both.TrySetResult();
                }
            }
        };

        Task<ApprovalAnswer?> first = Channel().AskAsync(HostQuestion("r1"), Token);
        Task<ApprovalAnswer?> second = Channel().AskAsync(HostQuestion("r2"), Token);
        await both.Task.WaitAsync(_patience, Token);
        _queue.Answer(arrived.Single(q => q.RequestId == "r2"), approve: true);
        _queue.Answer(arrived.Single(q => q.RequestId == "r1"), approve: false);

        ApprovalAnswer one = (await first.WaitAsync(_patience, Token))!;
        ApprovalAnswer two = (await second.WaitAsync(_patience, Token))!;
        two.Outcome?.Dispose();
        Assert.Equal(("r1", ApprovalDecision.Denied), (one.RequestId, one.Decision));
        Assert.Equal(("r2", ApprovalDecision.Granted), (two.RequestId, two.Decision));
    }

    [Fact]
    public async Task AFullQueue_SaysSoInTheHandshake_AndTheHostAsksElsewhere()
    {
        var queue = new ApprovalQueue(_clock, capacity: 1);
        string pipe = _pipe + ".full";
        await using ApprovalServer server = Server(queue, pipe);
        Assert.True(server.Start());
        Task<Question> arrival = Arrival(queue);
        Task<ApprovalAnswer?> first = Channel(pipe).AskAsync(HostQuestion("r1"), Token);
        Question waiting = await arrival.WaitAsync(_patience, Token);

        Assert.Null(await Channel(pipe).AskAsync(HostQuestion("r2"), Token));

        Assert.Equal([waiting], queue.Pending);
        queue.Answer(waiting, approve: false);
        await first.WaitAsync(_patience, Token);
    }

    [Fact]
    public async Task AClientThatDoesNotRunTheHostBesideTheWorkbench_IsRefusedBeforeAnything()
    {
        string pipe = _pipe + ".mismatch";
        await using ApprovalServer server = Server(_queue, pipe, expectedClient: Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"));
        Assert.True(server.Start());
        await using var raw = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await raw.ConnectAsync(TimeSpan.FromSeconds(5), Token);

        // The Workbench hangs up at once, so even sending the handshake may fail.
        await SendOrEndAsync(raw, WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(1, 1, "test", Environment.ProcessId)));

        Assert.Null(await ReceiveOrEndAsync(raw));
        Assert.Null(await Channel(pipe).AskAsync(HostQuestion(), Token));

        Assert.Empty(_queue.Pending);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!, "eplanmcp.exe"), ApprovalServer.ClientBeside(AppContext.BaseDirectory));
    }

    [Fact]
    public async Task ANameAlreadyTaken_ByAnotherWorkbenchOrProgram_IsReported_AndServesNothing()
    {
        await using ApprovalServer second = Server(new ApprovalQueue(_clock));
        string squatted = _pipe + ".squatted";
        using var squatter = new NamedPipeServerStream(squatted, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using ApprovalServer late = Server(new ApprovalQueue(_clock), squatted);

        Assert.False(second.Start());
        Assert.False(late.Start());
    }

    [Fact]
    public async Task ThePipe_IsOwnedByThisUser_AdmitsOnlyThem_AndNoNetworkLogon()
    {
        string pipe = _pipe + ".acl";
        await using var server = new ApprovalServer(_queue, pipe, Environment.ProcessPath!, _clock, NullLogger.Instance);
        Assert.True(server.Start());
        await using var probe = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await probe.ConnectAsync(TimeSpan.FromSeconds(5), Token);
        PipeSecurity security = probe.GetAccessControl();
        using WindowsIdentity me = WindowsIdentity.GetCurrent();
        AuthorizationRule[] rules = [.. security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<AuthorizationRule>()];

        Assert.Equal(me.User, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(2, rules.Length);
        Assert.Contains(rules.OfType<PipeAccessRule>(), r => r.IdentityReference == me.User && r.AccessControlType == AccessControlType.Allow);
        Assert.Contains(rules.OfType<PipeAccessRule>(), r => r.IdentityReference == new SecurityIdentifier(WellKnownSidType.NetworkSid, null) && r.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public async Task QuittingTheWorkbench_ClosesItsQuestions_AndTheHostTakesThemAsCancelled()
    {
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> asking = Channel().AskAsync(HostQuestion(), Token);
        Question question = await arrival.WaitAsync(_patience, Token);

        await _server.DisposeAsync();

        Assert.Equal(QuestionEnd.Closed, question.End);
        Assert.Equal(ApprovalDecision.Cancelled, (await asking.WaitAsync(_patience, Token))!.Decision);
    }

    public static TheoryData<string> BrokenQuestions => new()
    {
        """{"jsonrpc":"2.0","id":2,"method":"approval.ask","params":{"requestId":"r1","note":"pre-approved"}}""",
        """{"jsonrpc":"2.0","id":2,"method":"approval.ask","params":{"requestId":"r1","requestId":"r2"}}""",
        """{"jsonrpc":"2.0","id":2,"method":"approval.ask","params":null}""",
    };

    [Theory]
    [MemberData(nameof(BrokenQuestions))]
    public async Task AQuestionThatIsNotStrictlyValid_GetsAnError_AndNeverReachesTheQueue(string ask)
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();
        await HandshakeAsync(pipe);

        await SendAsync(pipe, Encoding.UTF8.GetBytes(ask));

        // An error when the message could be read, else the connection closes (a duplicate field breaks the frame).
        RpcMessage? reply = await ReceiveOrEndAsync(pipe);
        Assert.True(reply is null || reply.Error?.Code == -32602, reply?.ToString());
        Assert.Empty(_queue.Pending);
        Assert.True(_queue.TryReserve(), "the place the handshake promised is free again");
    }

    [Theory]
    [InlineData("", "f1", 120)]
    [InlineData("r1", "", 120)]
    [InlineData("r1", "f1", 3600)]
    public async Task AQuestionWithoutItsBinding_OrWithADeadlineNoHostGives_IsRefused(string requestId, string fingerprint, int seconds)
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();
        await HandshakeAsync(pipe);

        await SendAsync(pipe, WorkbenchJson.Request(2, WorkbenchProtocol.ApprovalAskMethod, Asks.Write(requestId, TimeSpan.FromSeconds(seconds)) with { Fingerprint = fingerprint }));

        Assert.Equal(-32602, (await ReceiveAsync(pipe))!.Error!.Code);
        Assert.Empty(_queue.Pending);
    }

    [Fact]
    public async Task ASecondQuestionWithARequestIdInTheQueue_IsRefused()
    {
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> first = Channel().AskAsync(HostQuestion("r1"), Token);
        Question waiting = await arrival.WaitAsync(_patience, Token);
        await using NamedPipeClientStream pipe = await ConnectAsync();
        await HandshakeAsync(pipe);

        await SendAsync(pipe, WorkbenchJson.Request(2, WorkbenchProtocol.ApprovalAskMethod, Asks.Write("r1")));

        Assert.Equal(-32600, (await ReceiveAsync(pipe))!.Error!.Code);
        Assert.Equal([waiting], _queue.Pending);
        _queue.Answer(waiting, approve: false);
        await first.WaitAsync(_patience, Token);
    }

    [Fact]
    public async Task ARequestBeforeTheHandshake_IsTurnedAway()
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await SendAsync(pipe, WorkbenchJson.Request(2, WorkbenchProtocol.ApprovalAskMethod, Asks.Write()));

        Assert.Equal(WorkbenchProtocol.NotHandshakenErrorCode, (await ReceiveAsync(pipe))!.Error!.Code);
        Assert.Null(await ReceiveOrEndAsync(pipe));
        Assert.Empty(_queue.Pending);
    }

    public static TheoryData<string> BrokenOpenings => new()
    {
        """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1,"hostVersion":"x","hostProcessId":1}}""",
        """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1,"hostVersion":"x","hostProcessId":0,"extra":true}}""",
        """{"jsonrpc":"2.0","method":"cancel","params":{"requestId":"r1"}}""",
        "not json",
    };

    /// <summary>A question only after a handshake that names this very process; anything else closes the connection.</summary>
    [Theory]
    [MemberData(nameof(BrokenOpenings))]
    public async Task ABrokenOpening_ClosesTheConnectionWithoutAnAnswer(string opening)
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await SendAsync(pipe, Encoding.UTF8.GetBytes(opening.Replace("\"hostProcessId\":0", $"\"hostProcessId\":{Environment.ProcessId}", StringComparison.Ordinal)));

        Assert.Null(await ReceiveOrEndAsync(pipe));
        Assert.Empty(_queue.Pending);
    }

    [Fact]
    public async Task AnOversizedFrame_ClosesTheConnection()
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await pipe.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {WorkbenchProtocol.MaxFrameBytes + 1}\r\n\r\n"), Token);

        Assert.Null(await ReceiveOrEndAsync(pipe));
    }

    [Fact]
    public async Task AHostOfAnotherProtocolVersion_IsToldWhichOnesThisWorkbenchSpeaks()
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await SendAsync(pipe, WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(3, 2, "9.0.0", Environment.ProcessId)));

        Assert.Equal(WorkbenchProtocol.UnsupportedVersionErrorCode, (await ReceiveAsync(pipe))!.Error!.Code);
        Assert.Null(await ReceiveOrEndAsync(pipe));
    }

    [Fact]
    public async Task AnOutcome_ForAnotherQuestion_OrBeforeAnyAnswer_ApprovesNothing()
    {
        Task<Question> arrival = Arrival();
        await using NamedPipeClientStream pipe = await ConnectAsync();
        await HandshakeAsync(pipe);
        await SendAsync(pipe, WorkbenchJson.Request(2, WorkbenchProtocol.ApprovalAskMethod, Asks.Write("r1")));
        Question question = await arrival.WaitAsync(_patience, Token);
        Task ended = Changed(question, q => q.End is not null);

        await SendAsync(pipe, WorkbenchJson.Notification(WorkbenchProtocol.ApprovalOutcomeMethod, new ApprovalOutcomeParams("r1", WriteExecution.Committed, null)));

        await ended.WaitAsync(_patience, Token);
        Assert.Equal((QuestionEnd.Disconnected, QuestionExecution.None), (question.End, question.Execution));
        Assert.Null(await ReceiveOrEndAsync(pipe));
    }

    private ApprovalServer Server(ApprovalQueue queue, string? pipe = null, string? expectedClient = null) =>
        new(queue, pipe ?? _pipe, expectedClient ?? Environment.ProcessPath!, _clock, NullLogger.Instance, _tokenOwner);

    /// <summary>The Host's channel on this test's pipe (or <paramref name="pipe"/>), expecting this test process as the Workbench.</summary>
    private WorkbenchChannel Channel(string? pipe = null) =>
        new(pipe ?? _pipe, Environment.ProcessPath!, () => "claude-code", _clock, NullLogger.Instance);

    private ApprovalQuestion HostQuestion(string requestId = "r1")
    {
        var plan = new WritePlan
        {
            Project = "Line1",
            ProjectPath = @"D:\Projects\Line1",
            Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
            Fingerprint = "f1",
        };
        var eplan = new EplanInstanceInfo { ProcessId = 4242, Version = "2026.0.3.25421", Variant = "Pro Panel" };
        return new ApprovalQuestion(requestId, "eplan_page_create", "h1", plan, ["create page =A1&EFS1/5"], _clock.GetUtcNow() + TimeSpan.FromSeconds(120))
        {
            Tier = ToolTier.Write,
            Eplan = eplan,
        };
    }

    private Task<Question> Arrival(ApprovalQueue? queue = null)
    {
        var arrived = new TaskCompletionSource<Question>(TaskCreationOptions.RunContinuationsAsynchronously);
        (queue ?? _queue).Changed += q =>
        {
            if (q.End is null)
            {
                arrived.TrySetResult(q);
            }
        };
        return arrived.Task;
    }

    private Task Changed(Question question, Func<Question, bool> condition)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Changed += q =>
        {
            if (q == question && condition(q))
            {
                changed.TrySetResult();
            }
        };
        return condition(question) ? Task.CompletedTask : changed.Task;
    }

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(5), Token);
        return pipe;
    }

    private static async Task HandshakeAsync(Stream pipe)
    {
        await SendAsync(pipe, WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(1, 1, "test", Environment.ProcessId)));
        Assert.True(WorkbenchJson.As<WorkbenchHandshakeResult>((await ReceiveAsync(pipe))!.Result).Accepting);
    }

    private static Task SendAsync(Stream pipe, byte[] message) => WorkbenchFraming.WriteAsync(pipe, message, Token);

    private static SecurityIdentifier TokenOwner()
    {
        using WindowsIdentity me = WindowsIdentity.GetCurrent();
        return me.Owner ?? me.User!;
    }

    private static async Task SendOrEndAsync(Stream pipe, byte[] message)
    {
        try
        {
            await SendAsync(pipe, message);
        }
        catch (IOException)
        {
            // the Workbench closed the connection first
        }
    }

    private static async Task<RpcMessage?> ReceiveAsync(Stream pipe) =>
        await WorkbenchFraming.ReadAsync(pipe, Token).WaitAsync(_patience, Token) is { } frame ? WorkbenchJson.Read(frame) : null;

    /// <summary>The next message; null when the Workbench closed the connection instead.</summary>
    private static async Task<RpcMessage?> ReceiveOrEndAsync(Stream pipe)
    {
        try
        {
            return await ReceiveAsync(pipe);
        }
        catch (IOException)
        {
            return null;
        }
    }
}

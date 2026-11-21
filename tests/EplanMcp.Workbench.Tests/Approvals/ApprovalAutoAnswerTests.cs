using System.IO.Pipes;
using System.Security.Principal;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// ADR-0065 decision 3, with the real Host channel against the real Workbench pipe server: a question is answered for
/// the engineer's click only from the Workbench's own Host, for an ordinary write's approval, with the tool and
/// arguments hash of a write the engineer started and has not finished, once. Every other case is asked as usual, and
/// a case that fails leaves the started write unclaimed.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ApprovalAutoAnswerTests : IAsyncDisposable
{
    private static readonly SecurityIdentifier _tokenOwner = TokenOwner();

    private readonly ManualTime _clock = new(Asks.Now);
    private readonly string _pipe = WorkbenchPipeName.For("S-1-5-21-0-0-0-1000", Path.Combine(Path.GetTempPath(), "eplanmcp-tests", Guid.NewGuid().ToString("N")));
    private readonly ApprovalQueue _queue;
    private readonly EngineerWrites _writes = new();
    private readonly ApprovalServer _server;
    private bool _ownChild = true;

    public ApprovalAutoAnswerTests()
    {
        _queue = new ApprovalQueue(_clock);
        _server = new ApprovalServer(_queue, _pipe, Environment.ProcessPath!, _clock, NullLogger.Instance, _tokenOwner, _writes, _ => _ownChild);
        Assert.True(_server.Start());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task AWriteTheEngineerStarted_IsApprovedWithoutAsking_AndTheHostHearsWhoStartedIt()
    {
        using IDisposable started = _writes.Begin("eplan_page_create", "h1");

        ApprovalAnswer answer = (await Channel().AskAsync(HostQuestion(), Token).WaitAsync(Token))!;
        answer.Outcome?.Dispose();

        Assert.Equal((ApprovalDecision.Granted, true), (answer.Decision, answer.StartedInWorkbench));
        Question question = Assert.Single(_queue.Answered);
        Assert.Equal((QuestionEnd.Approved, true), (question.End, question.StartedInWorkbench));
    }

    [Fact]
    public async Task AQuestionFromAnotherHost_IsAsked_EvenWithTheSameToolAndHash()
    {
        _ownChild = false;
        using IDisposable started = _writes.Begin("eplan_page_create", "h1");

        await AskedAsync(HostQuestion());

        _ownChild = true;
        await AutoAsync(HostQuestion("r2")); // the started write was not used up by the other Host's question
    }

    [Theory]
    [InlineData("eplan_page_create", "other-hash")]
    [InlineData("eplan_page_rename", "h1")]
    public async Task ADifferentHashOrTool_IsAsked(string tool, string hash)
    {
        using IDisposable started = _writes.Begin(tool, hash);

        await AskedAsync(HostQuestion());
    }

    [Fact]
    public async Task ADestructiveWrite_IsAlwaysAsked_AndLeavesTheStartedWriteUnclaimed()
    {
        using IDisposable started = _writes.Begin("eplan_page_create", "h1");

        await AskedAsync(HostQuestion() with { Tier = ToolTier.Destructive });

        await AutoAsync(HostQuestion("r2"));
    }

    [Fact]
    public async Task ReleasingASuspension_IsAlwaysAsked_ThenTheWritesOwnApprovalIsAnswered()
    {
        using IDisposable started = _writes.Begin("eplan_page_create", "h1");
        var suspended = new Suspension(@"D:\Projects\Line1", null, null, "eplan_page_delete", "earlier", _clock.GetUtcNow().AddHours(-1), SuspensionReason.OutcomeUnknown, ["delete page =A1&EFS1/4"]);

        await AskedAsync(HostQuestion() with { Release = suspended });

        await AutoAsync(HostQuestion());
    }

    [Fact]
    public async Task AStartedWrite_AnswersOneQuestionOnly()
    {
        using IDisposable started = _writes.Begin("eplan_page_create", "h1");

        await AutoAsync(HostQuestion("r1"));
        await AskedAsync(HostQuestion("r2"));
    }

    [Fact]
    public async Task AWriteThatEnded_AnswersNothing()
    {
        _writes.Begin("eplan_page_create", "h1").Dispose();

        await AskedAsync(HostQuestion());
    }

    [Fact]
    public async Task AHostSpeakingProtocol1_IsAsked_SoItsAuditNeverMissesWhoStartedTheWrite()
    {
        using IDisposable started = _writes.Begin("eplan_page_create", "h1");
        Task<Question> arrival = Arrival();
        await using var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(Token);
        await WorkbenchFraming.WriteAsync(pipe, WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(1, 1, "old", Environment.ProcessId)), Token);
        Assert.Equal(1, WorkbenchJson.As<WorkbenchHandshakeResult>(WorkbenchJson.Read((await WorkbenchFraming.ReadAsync(pipe, Token))!).Result).ProtocolVersion);

        await WorkbenchFraming.WriteAsync(pipe, WorkbenchJson.Request(2, WorkbenchProtocol.ApprovalAskMethod, Asks.Write("r1")), Token);
        Question question = await arrival.WaitAsync(Token);
        Assert.True(_queue.Answer(question, approve: true));

        ApprovalAskResult result = WorkbenchJson.As<ApprovalAskResult>(WorkbenchJson.Read((await WorkbenchFraming.ReadAsync(pipe, Token))!).Result);
        Assert.Null(result.InitiatedBy);
        Assert.False(question.StartedInWorkbench);
    }

    /// <summary>The question waits for the engineer: answering it by hand works, so nobody answered it first.</summary>
    private async Task AskedAsync(ApprovalQuestion question)
    {
        Task<Question> arrival = Arrival();
        Task<ApprovalAnswer?> asking = Channel().AskAsync(question, Token);
        Question shown = await arrival.WaitAsync(Token);

        Assert.True(_queue.Answer(shown, approve: false));
        ApprovalAnswer answer = (await asking.WaitAsync(Token))!;
        Assert.Equal((ApprovalDecision.Denied, false), (answer.Decision, answer.StartedInWorkbench));
        Assert.False(shown.StartedInWorkbench);
    }

    private async Task AutoAsync(ApprovalQuestion question)
    {
        ApprovalAnswer answer = (await Channel().AskAsync(question, Token).WaitAsync(Token))!;
        answer.Outcome?.Dispose();
        Assert.Equal((ApprovalDecision.Granted, true), (answer.Decision, answer.StartedInWorkbench));
    }

    private WorkbenchChannel Channel() => new(_pipe, Environment.ProcessPath!, () => "EplanMcp Workbench", _clock, NullLogger.Instance);

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

    private Task<Question> Arrival()
    {
        var arrived = new TaskCompletionSource<Question>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Changed += q =>
        {
            if (q.End is null)
            {
                arrived.TrySetResult(q);
            }
        };
        return arrived.Task;
    }

    private static SecurityIdentifier TokenOwner()
    {
        using WindowsIdentity me = WindowsIdentity.GetCurrent();
        return me.Owner ?? me.User!;
    }
}

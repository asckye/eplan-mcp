using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Reporting;
using EplanMcp.Evals.Run;
using EplanMcp.Evals.Tasks;
using EplanMcp.Evals.Tests.Server;

namespace EplanMcp.Evals.Tests.Run;

[Trait("Size", "Small")]
public sealed class EvalRunTests
{
    private static readonly HashSet<string> _tools = ["eplan_project_get"];

    private static readonly EvalTask _pageCount = TaskLoader.Load("page-count", """
        id: page-count
        set: dev
        prompt: 这个项目有多少页？
        answer: { type: object, properties: { pages: { type: integer } } }
        oracle: { pages: { tool: eplan_project_get, pick: $.counts.pages } }
        checks: [{ answer: $.pages, equals: oracle.pages }]
        """).Task!;

    [Fact]
    public async Task Run_GradesEveryAttemptAgainstTheOracle()
    {
        var runner = new FakeRunner(Answer("""{"pages":321}"""), Answer("""{"pages":320}"""), Failed("more than 20 tool calls"));
        var records = new List<TaskRecord>();
        using var progress = new StringWriter { NewLine = "\n" };

        await EvalRun.RunAsync([_pageCount], 3, Project(321), _tools, runner, progress, records, TimeSpan.Zero, () => 0, TestContext.Current.CancellationToken);

        TaskRecord task = Assert.Single(records);
        Assert.Equal(("page-count", "dev", null), (task.Id, task.Set, task.Invalid));
        Assert.Equal([Outcome.Solved, Outcome.Failed, Outcome.Failed], task.Attempts.Select(a => a.Outcome));
        Assert.Equal(["$.pages equals: 320 is not 321"], task.Attempts[1].Failures);
        Assert.Equal("more than 20 tool calls", task.Attempts[2].Reason);
        Assert.Equal([1, 2, 3], runner.Attempts);
        Assert.StartsWith("page-count #1: solved (0 calls, 2 s, 0.00 USD)\npage-count #2: failed, the answer failed checks", progress.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_ATaskWhoseOracleFails_GetsNoAttempt()
    {
        var runner = new FakeRunner();
        var records = new List<TaskRecord>();
        var client = new FakeToolClient().Fail("eplan_project_get", "EPLAN_BUSY: a dialog is open");

        await EvalRun.RunAsync([_pageCount], 2, client, _tools, runner, TextWriter.Null, records, TimeSpan.Zero, () => 0, TestContext.Current.CancellationToken);

        TaskRecord task = Assert.Single(records);
        Assert.Equal("oracle 'pages': eplan_project_get failed: EPLAN_BUSY: a dialog is open", task.Invalid);
        Assert.Empty(task.Attempts);
        Assert.Empty(runner.Attempts);
    }

    [Fact]
    public async Task Run_Cancelled_KeepsTheAttemptsThatFinished()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runner = new FakeRunner(Answer("""{"pages":321}""")) { OnSecond = cancel.Cancel };
        var records = new List<TaskRecord>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EvalRun.RunAsync([_pageCount], 3, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, () => 0, cancel.Token));

        Assert.Equal([Outcome.Solved], Assert.Single(records).Attempts.Select(a => a.Outcome));
    }

    [Fact]
    public async Task Run_AnAttemptInvalidBeforeTheModelStarted_IsRetriedOnceAndBothAreReported()
    {
        var runner = new FakeRunner(ApiError("server_error"), ApiError("server_error"), Answer("""{"pages":321}"""));
        var records = new List<TaskRecord>();
        using var progress = new StringWriter { NewLine = "\n" };

        await EvalRun.RunAsync([_pageCount], 2, Project(321), _tools, runner, progress, records, TimeSpan.Zero, () => 0, TestContext.Current.CancellationToken);

        // The retry of attempt 1 fails the same way and is not retried again; attempt 2 goes on as usual.
        Assert.Equal([(1, false), (1, true), (2, false)], runner.Runs);
        Assert.Equal(
            [(1, Outcome.Invalid, false), (1, Outcome.Invalid, true), (2, Outcome.Solved, false)],
            Assert.Single(records).Attempts.Select(a => (a.Attempt, a.Outcome, a.Retry)));
        Assert.Contains("page-count #1: retrying in 0 s\npage-count #1 (retry): invalid, API error server_error", progress.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_AMissingLogin_IsNotRetried()
    {
        var runner = new FakeRunner(ApiError("authentication_failed"));
        var records = new List<TaskRecord>();

        await EvalRun.RunAsync([_pageCount], 1, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, () => 0, TestContext.Current.CancellationToken);

        Assert.Equal([(1, false)], runner.Runs);
    }

    [Fact]
    public async Task Run_ACommittedWriteTheEvalsAuditShows_IsGradedAsUsual()
    {
        int audited = 0;
        var runner = new FakeRunner(Answer("""{"pages":321}""", committed: true)) { OnRun = () => audited++ };
        var records = new List<TaskRecord>();

        await EvalRun.RunAsync([_pageCount], 1, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, () => audited, TestContext.Current.CancellationToken);

        Assert.Equal(Outcome.Solved, Assert.Single(Assert.Single(records).Attempts).Outcome);
    }

    [Fact]
    public async Task Run_ACommittedWriteTheEvalsAuditDoesNotShow_IsInvalidAndStopsTheRun()
    {
        var runner = new FakeRunner(Answer("""{"pages":321}""", committed: true), Answer("""{"pages":321}"""));
        var records = new List<TaskRecord>();

        InvalidOperationException stop = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EvalRun.RunAsync([_pageCount], 2, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, () => 3, TestContext.Current.CancellationToken));

        AttemptRecord attempt = Assert.Single(Assert.Single(records).Attempts);
        Assert.Equal((Outcome.Invalid, stop.Message), (attempt.Outcome, attempt.Reason));
        Assert.StartsWith("not isolated: eplanmcp committed a write", stop.Message, StringComparison.Ordinal);
        Assert.Equal([1], runner.Attempts);
    }

    [Fact]
    public async Task Run_AnAuditLogThatCannotBeRead_DoesNotMakeAnAttemptInvalid()
    {
        var runner = new FakeRunner(Answer("""{"pages":321}""", committed: true));
        var records = new List<TaskRecord>();

        await EvalRun.RunAsync([_pageCount], 1, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, () => -1, TestContext.Current.CancellationToken);

        Assert.Equal(Outcome.Solved, Assert.Single(Assert.Single(records).Attempts).Outcome);
    }

    private static FakeToolClient Project(int pages) =>
        new FakeToolClient().Answer("eplan_project_get", """{"counts":{"pages":N}}""".Replace("N", pages.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

    private static AgentAttempt Answer(string json, bool committed = false)
    {
        var trace = new AgentTrace();
        trace.Read("""{"type":"system","subtype":"init","tools":["StructuredOutput","mcp__eplan__eplan_project_get"],"mcp_servers":[{"name":"eplan","status":"connected"}]}""");
        if (committed)
        {
            trace.Read("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"w","name":"mcp__eplan__eplan_page_create","input":{}}]},"parent_tool_use_id":null}""");
            trace.Read("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"w","content":"{\"execution\":\"committed\"}"}]},"parent_tool_use_id":null}""");
        }

        trace.Read($$"""{"type":"result","subtype":"success","is_error":false,"structured_output":{{json}}}""");
        return AgentAttempt.From(trace, null, 0, string.Empty, TimeSpan.FromSeconds(2));
    }

    private static AgentAttempt ApiError(string error)
    {
        var trace = new AgentTrace();
        trace.Read("""{"type":"system","subtype":"init","tools":["StructuredOutput","mcp__eplan__eplan_project_get"],"mcp_servers":[{"name":"eplan","status":"connected"}]}""");
        trace.Read($$"""{"type":"assistant","message":{"content":[{"type":"text","text":"try later"}]},"parent_tool_use_id":null,"error":"{{error}}"}""");
        trace.Read("""{"type":"result","subtype":"success","is_error":true,"terminal_reason":"api_error","total_cost_usd":0,"result":"try later"}""");
        return AgentAttempt.From(trace, null, 1, string.Empty, TimeSpan.FromSeconds(9));
    }

    private static AgentAttempt Failed(string reason) =>
        AgentAttempt.From(new AgentTrace(), new AgentStop(AttemptStatus.Failed, reason), 1, string.Empty, TimeSpan.FromSeconds(60));

    private sealed class FakeRunner(params AgentAttempt[] attempts) : IAttemptRunner
    {
        private readonly Queue<AgentAttempt> _attempts = new(attempts);

        public List<(int Attempt, bool Retry)> Runs { get; } = [];

        public List<int> Attempts => [.. Runs.Select(r => r.Attempt)];

        public Action? OnSecond { get; init; }

        /// <summary>Runs during each attempt, as the model's writes would.</summary>
        public Action? OnRun { get; init; }

        public Task<AgentAttempt> RunAsync(EvalTask task, int attempt, bool retry, CancellationToken cancellationToken)
        {
            Runs.Add((attempt, retry));
            OnRun?.Invoke();
            if (attempt == 2 && OnSecond is not null)
            {
                OnSecond();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(_attempts.Dequeue());
        }
    }
}

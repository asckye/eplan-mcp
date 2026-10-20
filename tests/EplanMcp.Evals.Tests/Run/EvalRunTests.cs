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

    private static readonly EvalTask _rename = TaskLoader.Load("rename", """
        id: rename
        set: dev
        fixture: copy
        prompt: 在项目 {project} 中把 =F+A2-KF1 改名为 =F+A2-KF19。
        answer: { type: object, properties: { done: { type: boolean } } }
        oracle: { parts: { tool: eplan_device_get, args: { tag: "=F+A2-KF1" }, pick: "$.parts[*].partNumber" } }
        after:
          tags: { tool: eplan_device_get, args: { tag: "=F+A2-KF19" }, pick: "$.functions[*].tag" }
          parts: { tool: eplan_device_get, args: { tag: "=F+A2-KF19" }, pick: "$.parts[*].partNumber" }
        checks:
          - { answer: $.done, equals: true }
          - { after: tags, set_equals: ["=F+A2-KF19"] }
          - { after: parts, set_equals: oracle.parts }
        """).Task!;

    private static readonly Dictionary<(string Task, int Attempt), AttemptCopy> _noCopies = [];

    [Fact]
    public async Task Run_GradesEveryAttemptAgainstTheOracle()
    {
        var runner = new FakeRunner(Answer("""{"pages":321}"""), Answer("""{"pages":320}"""), Failed("more than 20 tool calls"));
        var records = new List<TaskRecord>();
        using var progress = new StringWriter { NewLine = "\n" };

        await EvalRun.RunAsync([_pageCount], 3, Project(321), _tools, runner, progress, records, TimeSpan.Zero, _noCopies, () => 0, TestContext.Current.CancellationToken);

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

        await EvalRun.RunAsync([_pageCount], 2, client, _tools, runner, TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => 0, TestContext.Current.CancellationToken);

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EvalRun.RunAsync([_pageCount], 3, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => 0, cancel.Token));

        Assert.Equal([Outcome.Solved], Assert.Single(records).Attempts.Select(a => a.Outcome));
    }

    [Fact]
    public async Task Run_AnAttemptInvalidBeforeTheModelStarted_IsRetriedOnceAndBothAreReported()
    {
        var runner = new FakeRunner(ApiError("server_error"), ApiError("server_error"), Answer("""{"pages":321}"""));
        var records = new List<TaskRecord>();
        using var progress = new StringWriter { NewLine = "\n" };

        await EvalRun.RunAsync([_pageCount], 2, Project(321), _tools, runner, progress, records, TimeSpan.Zero, _noCopies, () => 0, TestContext.Current.CancellationToken);

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

        await EvalRun.RunAsync([_pageCount], 1, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => 0, TestContext.Current.CancellationToken);

        Assert.Equal([(1, false)], runner.Runs);
    }

    [Fact]
    public async Task Run_TheAfterCallsOfAnAnsweredAttempt_AreGradedWithIt()
    {
        EvalTask task = TaskLoader.Load("refuse", """
            id: refuse
            set: dev
            prompt: 把 =GAA+A1-FC1 改名为 =GAA+A1-FC9
            answer: { type: object, properties: { done: { type: boolean } } }
            after: { tag: { tool: eplan_device_get, args: { tag: "=GAA+A1-FC1" }, pick: $.device.tag } }
            checks:
              - { answer: $.done, equals: false }
              - { after: tag, equals: "=GAA+A1-FC1" }
            """).Task!;
        FakeToolClient client = new FakeToolClient()
            .Answer("eplan_device_get", """{"device":{"tag":"=GAA+A1-FC1"}}""")
            .Fail("eplan_device_get", """{"error":{"reason":"DEVICE_NOT_FOUND"}}""");
        var runner = new FakeRunner(Answer("""{"done":false}"""), Failed("more than 20 tool calls"), Answer("""{"done":false}"""));
        var records = new List<TaskRecord>();

        await EvalRun.RunAsync([task], 3, client, new HashSet<string> { "eplan_device_get" }, runner, TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => 0, TestContext.Current.CancellationToken);

        // The failed attempt has no answer to grade, so no after call is made for it.
        IReadOnlyList<AttemptRecord> attempts = Assert.Single(records).Attempts;
        Assert.Equal([Outcome.Solved, Outcome.Failed, Outcome.Failed], attempts.Select(a => a.Outcome));
        Assert.StartsWith("after.tag equals: $.device.tag matches nothing in the result of eplan_device_get: {\"error\"", Assert.Single(attempts[2].Failures), StringComparison.Ordinal);
        Assert.Equal(2, client.Calls.Count);
    }

    [Fact]
    public async Task Run_AfterCallsThatFail_StopTheRunButTheAttemptIsReported()
    {
        EvalTask task = TaskLoader.Load("refuse", """
            id: refuse
            set: dev
            prompt: 把 =GAA+A1-FC1 改名为 =GAA+A1-FC9
            answer: { type: object, properties: { done: { type: boolean } } }
            after: { tag: { tool: eplan_device_get, args: { tag: "=GAA+A1-FC1" }, pick: $.device.tag } }
            checks: [{ after: tag, equals: "=GAA+A1-FC1" }]
            """).Task!;
        var records = new List<TaskRecord>();

        // No answer is scripted: the fake throws as a client whose eplanmcp has gone would.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EvalRun.RunAsync([task], 2, new FakeToolClient(), new HashSet<string> { "eplan_device_get" }, new FakeRunner(Answer("""{"done":false}""")), TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => 0, TestContext.Current.CancellationToken));

        AttemptRecord attempt = Assert.Single(Assert.Single(records).Attempts);
        Assert.Equal((Outcome.Invalid, "its after calls failed: No answer scripted for eplan_device_get."), (attempt.Outcome, attempt.Reason));
    }

    [Fact]
    public async Task Run_ATaskOnACopy_AsksAndChecksEachAttemptOnItsOwnCopy()
    {
        var copies = new Dictionary<(string Task, int Attempt), AttemptCopy>
        {
            [("rename", 1)] = new("Eval_r1_01", @"C:\s\copies\01\Eval_r1_01"),
            [("rename", 2)] = new("Eval_r1_02", @"C:\s\copies\02\Eval_r1_02", "it was not opened in EPLAN within 20 minutes"),
        };
        FakeToolClient client = new FakeToolClient()
            .Answer("eplan_device_get", """{"parts":[{"partNumber":"SIE.3RH2122-1HB40"}]}""")
            .Answer("eplan_device_get", """{"functions":[{"tag":"=F+A2-KF19"},{"tag":"=F+A2-KF19"}]}""")
            .Answer("eplan_device_get", """{"parts":[{"partNumber":"SIE.3RH2122-1HB40"}]}""");
        var runner = new FakeRunner(Answer("""{"done":true}"""));
        var records = new List<TaskRecord>();

        await EvalRun.RunAsync([_rename], 2, client, new HashSet<string> { "eplan_device_get" }, runner, TextWriter.Null, records, TimeSpan.Zero, copies, () => 0, TestContext.Current.CancellationToken);

        Assert.Equal(["在项目 Eval_r1_01 中把 =F+A2-KF1 改名为 =F+A2-KF19。"], runner.Prompts);
        Assert.Equal(3, client.Calls.Count);
        Assert.All(client.Calls, c => Assert.Contains("\"project\":\"C:\\\\s\\\\copies\\\\01\\\\Eval_r1_01\"", c, StringComparison.Ordinal));
        Assert.Equal(
            [(Outcome.Solved, null), (Outcome.Invalid, "the copy: it was not opened in EPLAN within 20 minutes")],
            Assert.Single(records).Attempts.Select(a => (a.Outcome, a.Reason)));
    }

    [Fact]
    public async Task Run_AnAttemptOnACopyWhoseOracleFails_IsInvalidAndTheNextOneStillRuns()
    {
        var copies = new Dictionary<(string Task, int Attempt), AttemptCopy>
        {
            [("rename", 1)] = new("Eval_r1_01", @"C:\s\Eval_r1_01"),
            [("rename", 2)] = new("Eval_r1_02", @"C:\s\Eval_r1_02"),
        };
        FakeToolClient client = new FakeToolClient()
            .Fail("eplan_device_get", "PROJECT_NOT_OPEN")
            .Answer("eplan_device_get", """{"parts":[]}""")
            .Fail("eplan_device_get", """{"error":{"reason":"DEVICE_NOT_FOUND"}}""")
            .Fail("eplan_device_get", """{"error":{"reason":"DEVICE_NOT_FOUND"}}""");
        var runner = new FakeRunner(Answer("""{"done":true}"""));
        var records = new List<TaskRecord>();

        await EvalRun.RunAsync([_rename], 2, client, new HashSet<string> { "eplan_device_get" }, runner, TextWriter.Null, records, TimeSpan.Zero, copies, () => 0, TestContext.Current.CancellationToken);

        IReadOnlyList<AttemptRecord> attempts = Assert.Single(records).Attempts;
        Assert.Equal((Outcome.Invalid, "oracle 'parts': eplan_device_get failed: PROJECT_NOT_OPEN"), (attempts[0].Outcome, attempts[0].Reason));
        Assert.Equal(Outcome.Failed, attempts[1].Outcome);
        Assert.Equal(["after.tags set_equals: missing \"=F+A2-KF19\""], attempts[1].Failures);
        Assert.Equal([2], runner.Attempts);
    }

    [Fact]
    public async Task Run_ACommittedWriteTheEvalsAuditShows_IsGradedAsUsual()
    {
        int audited = 0;
        var runner = new FakeRunner(Answer("""{"pages":321}""", committed: true)) { OnRun = () => audited++ };
        var records = new List<TaskRecord>();

        await EvalRun.RunAsync([_pageCount], 1, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => audited, TestContext.Current.CancellationToken);

        Assert.Equal(Outcome.Solved, Assert.Single(Assert.Single(records).Attempts).Outcome);
    }

    [Fact]
    public async Task Run_ACommittedWriteTheEvalsAuditDoesNotShow_IsInvalidAndStopsTheRun()
    {
        var runner = new FakeRunner(Answer("""{"pages":321}""", committed: true), Answer("""{"pages":321}"""));
        var records = new List<TaskRecord>();

        InvalidOperationException stop = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EvalRun.RunAsync([_pageCount], 2, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => 3, TestContext.Current.CancellationToken));

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

        await EvalRun.RunAsync([_pageCount], 1, Project(321), _tools, runner, TextWriter.Null, records, TimeSpan.Zero, _noCopies, () => -1, TestContext.Current.CancellationToken);

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

        public List<string> Prompts { get; } = [];

        public Task<AgentAttempt> RunAsync(EvalTask task, int attempt, bool retry, CancellationToken cancellationToken)
        {
            Runs.Add((attempt, retry));
            Prompts.Add(task.Prompt);
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

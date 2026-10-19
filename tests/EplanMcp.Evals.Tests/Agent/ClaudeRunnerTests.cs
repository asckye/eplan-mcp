using System.Diagnostics;
using System.Text;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Agent;

/// <summary>
/// Runs the real process handling against a stand-in for claude: a batch file that saves its stdin, prints a canned
/// stream and, when asked to, keeps running the way a long model turn does.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ClaudeRunnerTests : IDisposable
{
    private const string Init = """{"type":"system","subtype":"init","tools":["StructuredOutput","mcp__eplan__eplan_page_list"],"mcp_servers":[{"name":"eplan","status":"connected"}]}""";
    private const string Answer = """{"type":"result","subtype":"success","is_error":false,"num_turns":2,"total_cost_usd":0.01,"structured_output":{"pages":12}}""";

    private static readonly EvalTask _task = TaskLoader.Load("t", """
        id: t
        set: dev
        prompt: =GAA+A1 下有多少页？
        answer: { type: object, properties: { pages: { type: integer } } }
        checks: [{ answer: $.pages, at_least: 1 }]
        budget: { max_tool_calls: 2 }
        """).Task!;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-claude-");

    public ClaudeRunnerTests()
    {
        File.WriteAllText(Fake, """
            @echo off
            findstr "^" > "%~dp0prompt.txt"
            type "%~dp0stream.jsonl"
            if exist "%~dp0hang" ping -n 30 127.0.0.1 > nul
            """.Replace("\n", "\r\n", StringComparison.Ordinal));
        Directory.CreateDirectory(Work);
        Directory.CreateDirectory(Output);
    }

    private string Fake => Path.Combine(_directory.FullName, "claude.cmd");

    private string Work => Path.Combine(_directory.FullName, "work");

    private string Output => Path.Combine(_directory.FullName, "output");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task Run_AnAnsweredStream_GivesTheAnswerAndKeepsTheTranscript()
    {
        string[] stream = [Init, Call("a"), Answer];
        Stream(stream);

        AgentAttempt attempt = await Run(_task, TestContext.Current.CancellationToken);

        Assert.Equal(AttemptStatus.Answered, attempt.Status);
        Assert.Equal("""{"pages":12}""", Comparison.Show(attempt.Answer));
        Assert.Single(attempt.Trace.Calls);
        Assert.True(attempt.Elapsed > TimeSpan.Zero);
        Assert.Equal(stream, File.ReadAllLines(Path.Combine(Output, ClaudeRunner.TranscriptFile)));
        Assert.True(File.Exists(Path.Combine(Output, ClaudeRunner.StderrFile)));
    }

    [Fact]
    public async Task Run_SendsThePromptOnStdin()
    {
        Stream([Init, Answer]);

        await Run(_task, TestContext.Current.CancellationToken);

        Assert.Equal(_task.Prompt, File.ReadAllText(Path.Combine(_directory.FullName, "prompt.txt"), Encoding.UTF8).TrimEnd());
    }

    [Fact]
    public async Task Run_MoreToolCallsThanTheBudget_StopsClaudeAtOnce()
    {
        Stream([Init, Call("a"), Call("b"), Call("c"), Answer], hang: true);
        var clock = Stopwatch.StartNew();

        AgentAttempt attempt = await Run(_task, TestContext.Current.CancellationToken);

        Assert.Equal((AttemptStatus.Failed, "more than 2 tool calls"), (attempt.Status, attempt.Reason));
        Assert.Equal(3, attempt.Trace.Calls.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Run_PastTheTimeLimit_StopsClaude()
    {
        Stream([Init, Call("a")], hang: true);
        var clock = Stopwatch.StartNew();

        AgentAttempt attempt = await Run(_task with { Budget = _task.Budget with { MaxDuration = TimeSpan.FromSeconds(1) } }, TestContext.Current.CancellationToken);

        Assert.Equal((AttemptStatus.Failed, "over the time limit of 0.02 minutes"), (attempt.Status, attempt.Reason));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Run_ASessionWithOtherTools_IsInvalidAndStopped()
    {
        Stream(["""{"type":"system","subtype":"init","tools":["Bash","mcp__eplan__eplan_page_list"],"mcp_servers":[{"name":"eplan","status":"connected"}]}""", Answer], hang: true);

        AgentAttempt attempt = await Run(_task, TestContext.Current.CancellationToken);

        Assert.Equal((AttemptStatus.Invalid, "not isolated: the model could use Bash, not only eplanmcp"), (attempt.Status, attempt.Reason));
    }

    [Fact]
    public async Task Run_CancelledByTheCaller_StopsClaudeAndThrows()
    {
        Stream([Init], hang: true);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(_task, cancel.Token));
    }

    private static string Call(string id) =>
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"ID","name":"mcp__eplan__eplan_page_list","input":{}}]},"parent_tool_use_id":null}"""
            .Replace("ID", id, StringComparison.Ordinal);

    private void Stream(string[] lines, bool hang = false)
    {
        File.WriteAllLines(Path.Combine(_directory.FullName, "stream.jsonl"), lines);
        if (hang)
        {
            File.WriteAllText(Path.Combine(_directory.FullName, "hang"), string.Empty);
        }
    }

    private Task<AgentAttempt> Run(EvalTask task, CancellationToken cancellationToken) =>
        ClaudeRunner.RunAsync(new ClaudeSetup(Fake, ClaudeCommand.DefaultModel, Path.Combine(_directory.FullName, "mcp.json")), task, Work, Output, cancellationToken);
}

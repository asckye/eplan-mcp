using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Agent;

[Trait("Size", "Small")]
public sealed class AgentAttemptTests
{
    private const string Init = """{"type":"system","subtype":"init","tools":["StructuredOutput","mcp__eplan__eplan_page_list"],"mcp_servers":[{"name":"eplan","status":"connected"}]}""";

    [Fact]
    public void From_AResultWithAStructuredAnswer_IsAnswered()
    {
        AgentAttempt attempt = From([Init, """{"type":"result","subtype":"success","is_error":false,"structured_output":{"pages":12}}"""]);

        Assert.Equal((AttemptStatus.Answered, null), (attempt.Status, attempt.Reason));
        Assert.Equal("""{"pages":12}""", Comparison.Show(attempt.Answer));
    }

    [Theory]
    [InlineData("""{"type":"result","subtype":"success","is_error":false,"result":"共 12 页。"}""", "no structured answer (success): 共 12 页。")]
    [InlineData("""{"type":"result","subtype":"error_max_structured_output_retries","is_error":true,"errors":["schema mismatch"]}""", "no structured answer (error_max_structured_output_retries): schema mismatch")]
    [InlineData("""{"type":"result","subtype":"error_max_budget_usd","is_error":true,"errors":["Reached maximum budget"]}""", "over the spending limit: Reached maximum budget")]
    [InlineData("""{"type":"result","subtype":"error_max_turns","is_error":true,"errors":["Reached maximum turns (13)"]}""", "out of turns: Reached maximum turns (13)")]
    [InlineData("""{"type":"result","subtype":"error_during_execution","is_error":true,"structured_output":{"pages":12},"errors":["tool crashed"]}""", "no structured answer (error_during_execution): tool crashed")]
    public void From_AResultWithoutAUsableAnswer_HasFailed(string result, string reason)
    {
        AgentAttempt attempt = From([Init, result]);

        Assert.Equal((AttemptStatus.Failed, reason), (attempt.Status, attempt.Reason));
        Assert.Null(attempt.Answer);
    }

    [Fact]
    public void From_AnAnswerAfterAnErrorTheRunRecoveredFrom_IsAnswered()
    {
        AgentAttempt attempt = From(
        [
            Init,
            """{"type":"assistant","message":{"content":[]},"parent_tool_use_id":null,"error":"overloaded"}""",
            """{"type":"result","subtype":"success","is_error":false,"structured_output":{"pages":12}}""",
        ]);

        Assert.Equal(AttemptStatus.Answered, attempt.Status);
    }

    [Fact]
    public void From_AnAnswerOverTheOutputLimit_HasFailed()
    {
        AgentAttempt attempt = From(
        [
            Init,
            """{"type":"assistant","message":{"content":[]},"parent_tool_use_id":null,"error":"max_output_tokens"}""",
            """{"type":"result","subtype":"success","is_error":true,"terminal_reason":"api_error","result":"Output limit reached"}""",
        ]);

        Assert.Equal((AttemptStatus.Failed, "over the output limit: Output limit reached"), (attempt.Status, attempt.Reason));
    }

    [Fact]
    public void From_KeepsTheElapsedTime()
    {
        Assert.Equal(TimeSpan.FromSeconds(42), From([Init]).Elapsed);
    }

    [Fact]
    public void StopFor_StopsWhenNotIsolatedOrOverTheToolCallBudget()
    {
        var budget = new Budget(1, TimeSpan.FromMinutes(1), 0.5m);
        const string Call = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"ID","name":"mcp__eplan__eplan_page_list","input":{}}]},"parent_tool_use_id":null}""";
        var trace = new AgentTrace();

        trace.Read(Init);
        trace.Read(Call.Replace("ID", "a", StringComparison.Ordinal));
        Assert.Null(ClaudeRunner.StopFor(trace, budget));
        trace.Read(Call.Replace("ID", "b", StringComparison.Ordinal));
        Assert.Equal(new AgentStop(AttemptStatus.Failed, "more than 1 tool calls"), ClaudeRunner.StopFor(trace, budget));

        var other = new AgentTrace();
        other.Read("""{"type":"system","subtype":"init","tools":["Read","mcp__eplan__eplan_page_list"],"mcp_servers":[{"name":"eplan","status":"connected"}]}""");
        Assert.Equal(new AgentStop(AttemptStatus.Invalid, "not isolated: the model could use Read, not only eplanmcp"), ClaudeRunner.StopFor(other, budget));
    }

    [Fact]
    public void From_AnApiError_IsInvalid()
    {
        AgentAttempt attempt = From(
        [
            Init,
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Not logged in"}]},"parent_tool_use_id":null,"error":"authentication_failed"}""",
            """{"type":"result","subtype":"success","is_error":true,"terminal_reason":"api_error","result":"Not logged in · Please run /login"}""",
        ]);

        Assert.Equal((AttemptStatus.Invalid, "API error authentication_failed: Not logged in · Please run /login"), (attempt.Status, attempt.Reason));
    }

    [Fact]
    public void IsRetryable_AnApiErrorBeforeTheModelDidAnything()
    {
        AgentAttempt attempt = From([Init, OAuthRefreshError, OAuthRefreshResult]);

        Assert.Equal((AttemptStatus.Invalid, "API error server_error: " + OAuthRefreshText), (attempt.Status, attempt.Reason));
        Assert.True(attempt.IsRetryable);
    }

    [Fact]
    public void IsRetryable_NotWithoutALoginNorOnceTheModelStarted()
    {
        AgentAttempt login = From(
        [
            Init,
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Not logged in"}]},"parent_tool_use_id":null,"error":"authentication_failed"}""",
            """{"type":"result","subtype":"success","is_error":true,"terminal_reason":"api_error","total_cost_usd":0,"result":"Not logged in · Please run /login"}""",
        ]);
        AgentAttempt spent = From([Init, OAuthRefreshError, OAuthRefreshResult.Replace("\"total_cost_usd\":0", "\"total_cost_usd\":0.02", StringComparison.Ordinal)]);
        AgentAttempt called = From(
        [
            Init,
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"mcp__eplan__eplan_page_list","input":{}}]},"parent_tool_use_id":null}""",
            OAuthRefreshError,
            OAuthRefreshResult,
        ]);
        AgentAttempt stopped = From([Init], new AgentStop(AttemptStatus.Invalid, "not isolated"));

        Assert.All([login, spent, called, stopped], a => Assert.Equal(AttemptStatus.Invalid, a.Status));
        Assert.All([login, spent, called, stopped], a => Assert.False(a.IsRetryable));
    }

    [Fact]
    public void From_NoResult_IsInvalidAndQuotesTheEndOfStderr()
    {
        AgentAttempt attempt = From([Init], stderr: new string('x', 400) + "MCP server crashed\n");

        Assert.Equal(AttemptStatus.Invalid, attempt.Status);
        Assert.StartsWith("claude exited with code 1 and no result: ...", attempt.Reason, StringComparison.Ordinal);
        Assert.EndsWith("xxMCP server crashed", attempt.Reason, StringComparison.Ordinal);
        Assert.Equal("claude exited with code 1 and no result", From([], stderr: " \n").Reason);
    }

    [Fact]
    public void From_AStopByTheRunner_WinsOverTheResult()
    {
        AgentAttempt attempt = From(
            [Init, """{"type":"result","subtype":"success","is_error":false,"structured_output":{"pages":12}}"""],
            new AgentStop(AttemptStatus.Failed, "more than 2 tool calls"));

        Assert.Equal((AttemptStatus.Failed, "more than 2 tool calls", null), (attempt.Status, attempt.Reason, attempt.Answer));
    }

    // The transient error of 2026-10-19, trimmed from the recorded run: another claude was refreshing the shared login.
    private const string OAuthRefreshText = "Failed to refresh OAuth token: another Claude Code process is refreshing it or exited mid-refresh. This is usually transient; retry in a minute";

    private const string OAuthRefreshError = """{"type":"assistant","message":{"model":"<synthetic>","role":"assistant","content":[{"type":"text","text":"Failed to refresh OAuth token: another Claude Code process is refreshing it or exited mid-refresh. This is usually transient; retry in a minute"}]},"parent_tool_use_id":null,"error":"server_error","is_api_error_message":true}""";

    private const string OAuthRefreshResult = """{"type":"result","subtype":"success","is_error":true,"terminal_reason":"api_error","total_cost_usd":0,"num_turns":1,"result":"Failed to refresh OAuth token: another Claude Code process is refreshing it or exited mid-refresh. This is usually transient; retry in a minute"}""";

    private static AgentAttempt From(string[] lines, AgentStop? stop = null, string stderr = "")
    {
        var trace = new AgentTrace();
        foreach (string line in lines)
        {
            trace.Read(line);
        }

        return AgentAttempt.From(trace, stop, exitCode: 1, stderr, TimeSpan.FromSeconds(42));
    }
}

using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Tests.Agent;

[Trait("Size", "Small")]
public sealed class AgentTraceTests
{
    // In the format of the Agent SDK documentation; replaced by a recorded run after the first real one (design 4.2).
    private static readonly string[] _answered =
    [
        """{"type":"system","subtype":"init","cwd":"C:\\evals\\work","session_id":"s1","tools":["StructuredOutput","mcp__eplan__eplan_device_get","mcp__eplan__eplan_device_list"],"mcp_servers":[{"name":"eplan","status":"connected","source":"dynamic"}],"model":"claude-sonnet-5-5","permissionMode":"dontAsk","slash_commands":[],"skills":[],"plugins":[]}""",
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"先查设备。"},{"type":"tool_use","id":"toolu_1","name":"mcp__eplan__eplan_device_get","input":{"tag":"-FC1"}}]},"parent_tool_use_id":null,"session_id":"s1"}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":[{"type":"text","text":"AMBIGUOUS_NAME"}],"is_error":true}]},"parent_tool_use_id":null,"session_id":"s1"}""",
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_2","name":"mcp__eplan__eplan_device_get","input":{"tag":"=GAA+A1-FC1"}},{"type":"tool_use","id":"toolu_3","name":"mcp__eplan__eplan_device_list","input":{}}]},"parent_tool_use_id":null,"session_id":"s1"}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_2","content":"{}"},{"type":"tool_result","tool_use_id":"toolu_3","content":"{}","is_error":false}]},"parent_tool_use_id":null,"session_id":"s1"}""",
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"toolu_4","name":"StructuredOutput","input":{"pages":["/3","/7"]}}]},"parent_tool_use_id":null,"session_id":"s1"}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_4","content":"Structured output provided successfully"}]},"parent_tool_use_id":null,"session_id":"s1"}""",
        """{"type":"result","subtype":"success","is_error":false,"duration_ms":41250,"duration_api_ms":30000,"num_turns":4,"result":"Done.","session_id":"s1","total_cost_usd":0.0841,"usage":{"input_tokens":12,"cache_creation_input_tokens":9000,"cache_read_input_tokens":30000,"output_tokens":800},"structured_output":{"pages":["/3","/7"]},"permission_denials":[],"terminal_reason":"completed"}""",
    ];

    // Recorded with Claude Code 2.1.299 while not logged in (api-notes C), shortened.
    private static readonly string[] _notLoggedIn =
    [
        """{"type":"system","subtype":"init","cwd":"C:\\evals\\work","session_id":"d97e","tools":["mcp__eplan__eplan_device_get"],"mcp_servers":[{"name":"eplan","status":"connected","source":"dynamic"}],"model":"claude-sonnet-5-5","permissionMode":"dontAsk","slash_commands":[],"apiKeySource":"none","claude_code_version":"2.1.299","output_style":"default","skills":[]}""",
        """{"type":"assistant","message":{"id":"2ac8","model":"<synthetic>","role":"assistant","stop_reason":"stop_sequence","type":"message","usage":{"input_tokens":0,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0},"content":[{"type":"text","text":"Not logged in · Please run /login"}]},"parent_tool_use_id":null,"session_id":"d97e","error":"authentication_failed","is_api_error_message":true}""",
        """{"duration_api_ms":0,"stop_reason":"stop_sequence","session_id":"d97e","total_cost_usd":0,"usage":{"input_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":0},"modelUsage":{},"permission_denials":[],"terminal_reason":"api_error","is_error":true,"num_turns":1,"subtype":"success","api_error_status":null,"result":"Not logged in · Please run /login","type":"result","duration_ms":40}""",
    ];

    [Fact]
    public void Read_AnAnsweredRun_CountsTheCallsButNotTheAnswer()
    {
        AgentTrace trace = Read(_answered);

        Assert.Equal(["eplan_device_get", "eplan_device_get", "eplan_device_list"], trace.Calls.Select(c => c.Tool));
        Assert.Equal([true, false, false], trace.Calls.Select(c => c.IsError));
        Assert.Equal(1, trace.Corrected);
        Assert.Null(trace.IsolationProblem());
        Assert.Null(trace.ApiError);
        Assert.Equal(0, trace.Unreadable);
    }

    [Fact]
    public void Read_TheResultEvent_GivesAnswerCostUsageAndDuration()
    {
        AgentResult result = Read(_answered).Result!;

        Assert.Equal(("success", false, "completed", "Done."), (result.Subtype, result.IsError, result.TerminalReason, result.Message));
        Assert.Equal("""{"pages":["/3","/7"]}""", Comparison.Show(result.Answer));
        Assert.Equal(0.0841m, result.CostUsd);
        Assert.Equal(new TokenUsage(12, 800, 30000, 9000), result.Usage);
        Assert.Equal(4, result.Turns);
        Assert.Equal(TimeSpan.FromMilliseconds(41250), result.Duration);
    }

    [Fact]
    public void Read_NotLoggedIn_IsAnApiErrorDespiteTheSuccessSubtype()
    {
        AgentTrace trace = Read(_notLoggedIn);

        Assert.Equal("authentication_failed", trace.ApiError);
        Assert.Equal(("success", true, "api_error"), (trace.Result!.Subtype, trace.Result.IsError, trace.Result.TerminalReason));
        Assert.Equal("Not logged in · Please run /login", trace.Result.Message);
        Assert.Null(trace.Result.Answer);
        Assert.Empty(trace.Calls);
    }

    [Fact]
    public void Read_AnErrorResult_TakesTheMessageFromErrors()
    {
        AgentTrace trace = Read(["""{"type":"result","subtype":"error_max_budget_usd","is_error":true,"num_turns":9,"total_cost_usd":0.52,"usage":{},"errors":["Reached maximum budget ($0.5)"],"terminal_reason":"budget_exhausted"}"""]);

        Assert.Equal(("error_max_budget_usd", "Reached maximum budget ($0.5)", 0.52m), (trace.Result!.Subtype, trace.Result.Message, trace.Result.CostUsd));
        Assert.Equal(new TokenUsage(0, 0, 0, 0), trace.Result.Usage);
    }

    [Fact]
    public void Read_SubagentMessages_AreNotCounted()
    {
        AgentTrace trace = Read(
        [
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_9","name":"mcp__eplan__eplan_page_list","input":{}}]},"parent_tool_use_id":"toolu_0"}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"mcp__eplan__eplan_page_list","input":{}}]},"parent_tool_use_id":null}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"x","is_error":true}]},"parent_tool_use_id":"toolu_0"}""",
        ]);

        ToolCall call = Assert.Single(trace.Calls);
        Assert.Equal(("toolu_1", null), (call.Id, call.IsError));
    }

    [Fact]
    public void Read_ResultsOfUnknownCallsAndOtherLines_AreIgnoredOrCounted()
    {
        AgentTrace trace = Read(
        [
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_7","content":"x"}]}}""",
            """{"type":"system","subtype":"api_retry","attempt":1,"max_retries":10,"retry_delay_ms":500,"error_status":529,"error":"overloaded"}""",
            "Waiting for background work to finish",
            "[1, 2]",
        ]);

        Assert.Empty(trace.Calls);
        Assert.Null(trace.Result);
        Assert.Equal(2, trace.Unreadable);
    }

    [Fact]
    public void Corrected_CountsOnlyFailuresFollowedBySuccessOfTheSameTool()
    {
        AgentTrace trace = Read(
        [
            Use("a", "eplan_page_get"), Outcome("a", true),
            Use("b", "eplan_page_list"), Outcome("b", false),
            Use("c", "eplan_device_get"), Outcome("c", true),
            Use("d", "eplan_device_get"), Outcome("d", true),
            Use("e", "eplan_device_get"), Outcome("e", false),
        ]);

        Assert.Equal(2, trace.Corrected);
    }

    [Theory]
    [InlineData("""["StructuredOutput","mcp__eplan__eplan_page_list"]""", "connected", null)]
    [InlineData("""["mcp__eplan__eplan_page_list","Bash"]""", "connected", "the model could use Bash, not only eplanmcp")]
    [InlineData("""["mcp__other__read"]""", "connected", "the model could use mcp__other__read, not only eplanmcp")]
    [InlineData("""["mcp__eplan__eplan_page_list"]""", "failed", "eplanmcp did not connect (status failed)")]
    [InlineData("[]", null, "eplanmcp did not connect (status missing)")]
    [InlineData("""["StructuredOutput"]""", "connected", "eplanmcp offers no tools")]
    public void IsolationProblem_AcceptsOnlyEplanmcpAndTheAnswerTool(string tools, string? status, string? problem)
    {
        string servers = status is null ? "[]" : $$"""[{"name":"eplan","status":"{{status}}"}]""";
        AgentTrace trace = Read([$$"""{"type":"system","subtype":"init","tools":{{tools}},"mcp_servers":{{servers}}}"""]);

        Assert.Equal(problem, trace.IsolationProblem());
    }

    [Fact]
    public void IsolationProblem_BeforeInit_IsNotKnownYet()
    {
        Assert.Null(new AgentTrace().IsolationProblem());
    }

    [Fact]
    public void IsolationProblem_AToolCallBeforeInit_IsOne()
    {
        AgentTrace trace = Read([Use("a", "eplan_page_list")]);

        Assert.Equal("a tool call came before the init event", trace.IsolationProblem());
    }

    private static string Use(string id, string tool) =>
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"ID","name":"mcp__eplan__TOOL","input":{}}]},"parent_tool_use_id":null}"""
            .Replace("ID", id, StringComparison.Ordinal).Replace("TOOL", tool, StringComparison.Ordinal);

    private static string Outcome(string id, bool error) =>
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"ID","content":"x","is_error":ERROR}]},"parent_tool_use_id":null}"""
            .Replace("ID", id, StringComparison.Ordinal).Replace("ERROR", error ? "true" : "false", StringComparison.Ordinal);

    private static AgentTrace Read(string[] lines)
    {
        var trace = new AgentTrace();
        foreach (string line in lines)
        {
            trace.Read(line);
        }

        return trace;
    }
}

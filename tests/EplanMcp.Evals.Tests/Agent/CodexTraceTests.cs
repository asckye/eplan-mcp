using System.Text.Json.Nodes;
using EplanMcp.Evals.Agent;

namespace EplanMcp.Evals.Tests.Agent;

[Trait("Size", "Small")]
public sealed class CodexTraceTests
{
    // Recorded with codex-cli 0.167 on 2026-11-10 against eplanmcp without EPLAN, shortened.
    private static readonly string[] _answered =
    [
        """{"type":"thread.started","thread_id":"01a1c335"}""",
        """{"type":"turn.started"}""",
        """{"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"I'll list the pages."}}""",
        """{"type":"item.started","item":{"id":"item_1","type":"mcp_tool_call","server":"eplan","tool":"eplan_page_list","arguments":{"project":"X"},"result":null,"error":null,"status":"in_progress"}}""",
        """{"type":"item.completed","item":{"id":"item_1","type":"mcp_tool_call","server":"eplan","tool":"eplan_page_list","arguments":{"project":"X"},"result":{"content":[{"type":"text","text":"{\"error\":{\"reason\":\"INVALID_ARGUMENT\"}}"}],"structured_content":null},"error":null,"status":"failed"}}""",
        """{"type":"item.started","item":{"id":"item_2","type":"mcp_tool_call","server":"eplan","tool":"eplan_page_create","arguments":{},"result":null,"error":null,"status":"in_progress"}}""",
        """{"type":"item.completed","item":{"id":"item_2","type":"mcp_tool_call","server":"eplan","tool":"eplan_page_create","arguments":{},"result":{"content":[{"type":"text","text":"{\"execution\": \"committed\"}"}],"structured_content":null},"error":null,"status":"completed"}}""",
        """{"type":"item.completed","item":{"id":"item_3","type":"agent_message","text":"{\"done\":true}"}}""",
        """{"type":"turn.completed","usage":{"input_tokens":96741,"cached_input_tokens":62208,"cache_write_input_tokens":0,"output_tokens":270,"reasoning_output_tokens":7}}""",
    ];

    private static AgentTrace Read(IEnumerable<string> lines)
    {
        var trace = new AgentTrace(codex: true);
        foreach (string line in lines)
        {
            trace.Read(line);
        }

        return trace;
    }

    [Fact]
    public void ToolCalls_Results_AndTheLastMessageAsAnswer()
    {
        AgentTrace trace = Read(_answered);

        Assert.Equal(["eplan_page_list", "eplan_page_create"], trace.Calls.Select(c => c.Tool));
        Assert.Equal((true, true, false), (trace.Calls[0].IsError == true, trace.Calls[0].ArgumentRejected, trace.Calls[0].Committed));
        Assert.Equal((false, true), (trace.Calls[1].IsError == true, trace.Calls[1].Committed));
        Assert.Null(trace.IsolationProblem());
        AgentResult result = Assert.IsType<AgentResult>(trace.Result);
        Assert.Equal("""{"done":true}""", result.Answer?.ToJsonString());
        Assert.Equal((96741L, 270L, 62208L, 0m), (result.Usage.Input, result.Usage.Output, result.Usage.CacheRead, result.CostUsd));
        Assert.Equal(AttemptStatus.Answered, AgentAttempt.From(trace, null, 0, string.Empty, TimeSpan.Zero).Status);
    }

    [Fact]
    public void ALastMessageThatIsNoJsonObject_IsNoAnswer()
    {
        AgentTrace trace = Read([.. _answered.Take(7), """{"type":"item.completed","item":{"id":"item_3","type":"agent_message","text":"Done."}}""", _answered[^1]]);

        Assert.Equal(AttemptStatus.Failed, AgentAttempt.From(trace, null, 0, string.Empty, TimeSpan.Zero).Status);
    }

    [Theory]
    [InlineData("""{"type":"item.started","item":{"id":"item_9","type":"command_execution","command":"dir","status":"in_progress"}}""", "command_execution")]
    [InlineData("""{"type":"item.started","item":{"id":"item_9","type":"mcp_tool_call","server":"other","tool":"x","status":"in_progress"}}""", "the MCP server other")]
    public void AnythingButEplanmcp_IsAnIsolationProblem(string line, string what)
    {
        AgentTrace trace = Read([_answered[0], line]);

        Assert.Equal($"the model could use {what}, not only eplanmcp", trace.IsolationProblem());
        Assert.Equal(AttemptStatus.Invalid, ClaudeRunner.StopFor(trace, new(60, TimeSpan.FromMinutes(5), 1))?.Status);
    }

    [Fact]
    public void AFailedTurn_IsInvalidAndRetriedOnceWhenNothingWasCalled()
    {
        AgentTrace trace = Read([_answered[0], """{"type":"turn.failed","error":{"message":"stream disconnected"}}"""]);

        AgentAttempt attempt = AgentAttempt.From(trace, null, 1, string.Empty, TimeSpan.Zero);

        Assert.Equal((AttemptStatus.Invalid, true), (attempt.Status, attempt.IsRetryable));
    }

    [Fact]
    public void StrictSchema_ClosesEveryObjectAndRequiresAllProperties()
    {
        JsonNode schema = JsonNode.Parse("""{"type":"object","required":["done"],"properties":{"done":{"type":"boolean"},"pages":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}}}}}}""")!;

        JsonNode strict = CodexCommand.StrictSchema(schema);

        Assert.Equal(
            """{"type":"object","required":["done","pages"],"properties":{"done":{"type":"boolean"},"pages":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}},"additionalProperties":false,"required":["name"]}}},"additionalProperties":false}""",
            strict.ToJsonString());
        Assert.DoesNotContain("additionalProperties", schema.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_ConfigureEplanmcpAsTheOnlyServerAndTheModelOnlyWhenGiven()
    {
        var setup = new ClaudeSetup(@"C:\codex\codex.exe", ClaudeCommand.DefaultModel, string.Empty, Codex: true, Eplanmcp: @"C:\a b\eplanmcp.exe", Home: @"C:\home");

        IReadOnlyList<string> arguments = CodexCommand.Arguments(setup, @"C:\work", @"C:\out\answer-schema.json");

        Assert.Contains("mcp_servers.eplan.command=\"C:\\\\a b\\\\eplanmcp.exe\"", arguments);
        Assert.Contains("mcp_servers.eplan.env.EPLANMCP_HOME=\"C:\\\\home\"", arguments);
        Assert.Contains("mcp_servers.eplan.default_tools_approval_mode=\"approve\"", arguments);
        Assert.Equal(["--ignore-user-config", "--ignore-rules", "read-only"], arguments.Where(a => a is "--ignore-user-config" or "--ignore-rules" or "read-only"));
        Assert.DoesNotContain("-m", arguments);
        Assert.Equal("-", arguments[^1]);
        Assert.Contains("gpt-5.5", CodexCommand.Arguments(setup with { Model = "gpt-5.5" }, @"C:\work", "s.json"));
    }
}

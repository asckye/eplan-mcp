using System.Text.Json.Nodes;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Agent;

[Trait("Size", "Small")]
public sealed class ClaudeCommandTests
{
    private static readonly EvalTask _task = TaskLoader.Load("t", """
        id: t
        set: dev
        prompt: -FC1 在哪几页？
        answer: { type: object, properties: { pages: { type: array } } }
        checks: [{ answer: $.pages, contains: "/3" }]
        budget: { max_tool_calls: 12, max_usd: 0.25 }
        """).Task!;

    [Fact]
    public void Arguments_IsolateTheSessionAndPassTheTaskLimits()
    {
        Assert.Equal(
            [
                "-p", "--output-format", "stream-json", "--verbose", "--no-session-persistence",
                "--restricted", "--tools", "", "--disable-slash-commands",
                "--strict-mcp-config", "--mcp-config", @"C:\evals\mcp.json",
                "--allowedTools", "mcp__eplan,StructuredOutput",
                "--permission-mode", "dontAsk", "--permission-prompts", "none",
                "--json-schema", """{"type":"object","properties":{"pages":{"type":"array"}}}""",
                "--model", "claude-sonnet-5-5",
                "--max-budget-usd", "0.25", "--max-turns", "17",
            ],
            ClaudeCommand.Arguments(_task, ClaudeCommand.DefaultModel, @"C:\evals\mcp.json"));
    }

    [Fact]
    public void Arguments_LeaveThePromptToStdin()
    {
        Assert.DoesNotContain(ClaudeCommand.Arguments(_task, "m", "c"), a => a.Contains("FC1", StringComparison.Ordinal));
    }

    [Fact]
    public void McpConfig_StartsOnlyEplanmcpOverStdioWithTheEvalsDataRoot()
    {
        JsonNode config = JsonNode.Parse(ClaudeCommand.McpConfig(@"C:\Tools\EplanMcp\eplanmcp.exe", @"C:\repo\.local\evals\r1\eplanmcp"))!;

        Assert.Equal(
            """{"mcpServers":{"eplan":{"type":"stdio","command":"C:\\Tools\\EplanMcp\\eplanmcp.exe","args":["serve"],"env":{"EPLANMCP_HOME":"C:\\repo\\.local\\evals\\r1\\eplanmcp"}}}}""",
            config.ToJsonString());
    }
}

using System.Text.Json;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Reporting;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Agent;

[Trait("Size", "Small")]
public sealed class ArgumentRetryTests
{
    [Theory]
    [InlineData("INVALID_ARGUMENT", true, true)]
    [InlineData("Input validation errors for tool: missing parameters", true, true)]
    [InlineData("InputValidationError: invalid input", true, true)]
    [InlineData("MCP error -32602: invalid params", true, true)]
    [InlineData("SCHEMA validation failed", true, true)]
    [InlineData("EPLAN_BUSY", true, false)]
    [InlineData("POLICY_DENIED", true, false)]
    [InlineData("INVALID_ARGUMENT appears in documentation", false, false)]
    public void CountsOnlyArgumentFailuresOncePerCall(string content, bool error, bool expected)
    {
        var trace = new AgentTrace();
        trace.Read(Use("a", "eplan_api_try"));
        string result = Result("a", content, error);
        trace.Read(result);
        trace.Read(result);
        Assert.Equal(expected ? 1 : 0, trace.ArgumentRetries);
        AgentAttempt run = new(AttemptStatus.Answered, null, null, trace, TimeSpan.Zero);
        AttemptRecord record = AttemptRecord.From(1, run, null);
        Assert.Equal(trace.ArgumentRetries, record.ArgumentRetries);
        var report = new RunReport(DateTimeOffset.UnixEpoch, "model", "claude", "eplan", [], [new("template", "dev", null, [], [record])]);
        Assert.Contains("argumentRetries", ReportWriter.Json(report), StringComparison.Ordinal);
        Assert.Contains("argument retries（目标 0）", ReportWriter.Markdown(report), StringComparison.Ordinal);
        Assert.Contains(expected ? "：1" : "：0", ReportWriter.Markdown(report), StringComparison.Ordinal);
    }

    [Fact]
    public void SequenceRequiresSuccessfulCallsInOrder()
    {
        var trace = new AgentTrace();
        trace.Read(Use("a", "eplan_api_try"));
        trace.Read(Result("a", "{}", false));
        trace.Read(Use("b", "eplan_api_get"));
        trace.Read(Result("b", "{}", false));
        string[] sequence = ["eplan_api_get", "eplan_api_try"];
        Assert.False(trace.Follows(sequence));
        trace.Read(Use("c", "eplan_api_try"));
        trace.Read(Result("c", "INVALID_ARGUMENT", true));
        Assert.False(trace.Follows(sequence));
        trace.Read(Use("d", "eplan_api_try"));
        trace.Read(Result("d", "{}", false));
        Assert.True(trace.Follows(sequence));
        Assert.Equal(1, trace.ArgumentRetries);
    }

    [Fact]
    public void SubagentFailuresAreExcluded()
    {
        var trace = new AgentTrace();
        trace.Read(Use("a", "eplan_api_try").Replace("\"type\":\"assistant\"", "\"parent_tool_use_id\":\"parent\",\"type\":\"assistant\"", StringComparison.Ordinal));
        trace.Read(Result("a", "INVALID_ARGUMENT", true));
        Assert.Equal(0, trace.ArgumentRetries);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("-1", false)]
    [InlineData("0.5", false)]
    [InlineData("201", false)]
    public void BudgetAllowsZeroButRejectsNegativeFractionalAndExcessiveLimits(string retries, bool valid)
    {
        TaskFile file = TaskLoader.Load("template", $$"""
            id: template
            set: dev
            prompt: test
            answer: { type: object, properties: { ok: { type: boolean } } }
            checks: [{ answer: $.ok, equals: true }]
            tool_sequence: [eplan_api_get, eplan_api_try]
            budget: { max_argument_retries: {{retries}} }
            """);
        Assert.Equal(valid, file.Task is not null);
        if (valid)
        { Assert.Equal(int.Parse(retries, System.Globalization.CultureInfo.InvariantCulture), file.Task!.Budget.MaxArgumentRetries); Assert.Equal(["eplan_api_get", "eplan_api_try"], file.Task.ToolSequence); }
    }

    private static string Use(string id, string tool) => JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "tool_use", id, name = "mcp__eplan__" + tool } } } });
    private static string Result(string id, string content, bool error) => JsonSerializer.Serialize(new { type = "user", message = new { content = new[] { new { type = "tool_result", tool_use_id = id, content, is_error = error } } } });
}

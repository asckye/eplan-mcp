using System.Text.Json;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Reporting;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Tests.Reporting;

[Trait("Size", "Small")]
public sealed class ReportTests
{
    private const string Init = """{"type":"system","subtype":"init","tools":["StructuredOutput","mcp__eplan__eplan_device_get"],"mcp_servers":[{"name":"eplan","status":"connected"}]}""";
    private const string Result = """{"type":"result","subtype":"success","is_error":false,"num_turns":4,"total_cost_usd":0.12,"duration_ms":30000,"usage":{"input_tokens":10,"cache_read_input_tokens":3000,"cache_creation_input_tokens":200,"output_tokens":400},"structured_output":{"pages":["/3"]}}""";

    [Fact]
    public void From_ASolvedAttempt_KeepsTheMetrics()
    {
        AgentAttempt run = Attempt(AttemptStatus.Answered, Use("a", "eplan_device_get"), ToolResult("a", true), Use("b", "eplan_device_get"), ToolResult("b", false), Use("c", "eplan_page_list"), ToolResult("c", false), Result);

        AttemptRecord record = AttemptRecord.From(1, run, new Verdict([], []));

        Assert.Equal((Outcome.Solved, null), (record.Outcome, record.Reason));
        Assert.Equal(new Dictionary<string, int> { ["eplan_device_get"] = 2, ["eplan_page_list"] = 1 }, record.ToolCalls);
        Assert.Equal((1, 1), (record.ErrorCalls, record.Corrected));
        Assert.Equal((3210L, 400L, 0.12m, 4), (record.InputTokens, record.OutputTokens, record.CostUsd, record.Turns));
        Assert.Equal(41.3, record.Seconds);
    }

    [Fact]
    public void From_TheVerdictDecidesForAnAnsweredAttempt()
    {
        AgentAttempt run = Attempt(AttemptStatus.Answered, Result);

        AttemptRecord failed = AttemptRecord.From(2, run, new Verdict(["$.pages set_equals: missing \"/7\""], []));
        AttemptRecord invalid = AttemptRecord.From(3, run, new Verdict([], ["$.pages set_equals: oracle 'pages' gave 2, but set_equals compares with a list"]));

        Assert.Equal((Outcome.Failed, "the answer failed checks"), (failed.Outcome, failed.Reason));
        Assert.Equal(["$.pages set_equals: missing \"/7\""], failed.Failures);
        Assert.Equal((Outcome.Invalid, "$.pages set_equals: oracle 'pages' gave 2, but set_equals compares with a list"), (invalid.Outcome, invalid.Reason));
    }

    [Theory]
    [InlineData(AttemptStatus.Failed, "failed")]
    [InlineData(AttemptStatus.Invalid, "invalid")]
    internal void From_AnAttemptWithoutAnAnswer_KeepsItsReason(AttemptStatus status, string outcome)
    {
        AttemptRecord record = AttemptRecord.From(1, Attempt(status), null);

        Assert.Equal((outcome, "why"), (record.Outcome, record.Reason));
        Assert.Equal((0L, 0m), (record.InputTokens, record.CostUsd));
    }

    [Fact]
    public void Summarize_CountsOnlyValidAttemptsButAllTheCost()
    {
        RunReport report = Report(
            Task("a", "dev", Record(Outcome.Solved, calls: 2, seconds: 10, cost: 0.1m), Record(Outcome.Failed, calls: 6, seconds: 30, cost: 0.2m)),
            Task("b", "dev", Record(Outcome.Solved, calls: 4, seconds: 20, cost: 0.1m), Record(Outcome.Invalid, calls: 40, seconds: 99, cost: 0.05m)),
            Task("c", "holdout", Record(Outcome.Invalid, calls: 0, seconds: 1, cost: 0m)));

        IReadOnlyList<SetSummary> sets = ReportWriter.Summarize(report);

        Assert.Equal(new SetSummary("dev", 2, 3, 2, 2.0 / 3, 4, 1000, 100, 20, 0.45m), sets[0]);
        Assert.Equal(new SetSummary("holdout", 1, 0, 0, null, null, null, null, null, 0m), sets[1]);
    }

    [Fact]
    public void Markdown_SummarizesInChineseAndListsWhatWentWrong()
    {
        RunReport report = Report(
            Task("device-pages", "dev", Record(Outcome.Solved), Record(Outcome.Failed, reason: "the answer failed checks", failures: ["$.pages set_equals: missing \"/7\""])),
            new TaskRecord("part-count", "holdout", "oracle 'parts': eplan_part_list failed: EPLAN_BUSY", ["oracle 'x': matched nothing"], [])) with
        { InvalidFiles = ["broken.yaml line 2: set 'nightly' is neither dev nor holdout"] };

        string markdown = ReportWriter.Markdown(report);

        Assert.Contains("| 开发集 | 1 | 2 | 1 | 50.0% | 3 | 1000 | 100 | 12 | 0.20 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 保留集 | 1 | 0 | 0 | — | — | — | — | — | 0.00 |", markdown, StringComparison.Ordinal);
        Assert.Contains("## 失败\n- `device-pages` 第 2 次：the answer failed checks\n  - $.pages set_equals: missing \"/7\"\n", markdown, StringComparison.Ordinal);
        Assert.Contains("## 无效（不计入成功率）\n- `part-count`：oracle 'parts': eplan_part_list failed: EPLAN_BUSY\n", markdown, StringComparison.Ordinal);
        Assert.Contains("## 无效的任务文件\n- broken.yaml line 2:", markdown, StringComparison.Ordinal);
        Assert.Contains("## 提示\n- `part-count`：oracle 'x': matched nothing\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_KeepsAMultiLineReasonOnItsListItem()
    {
        string markdown = ReportWriter.Markdown(Report(Task("a", "dev", Record(Outcome.Invalid, reason: "claude exited with code 1 and no result: line one\r\nline two\n"))));

        Assert.Contains("- `a` 第 1 次：claude exited with code 1 and no result: line one line two\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_LeavesOutEmptySections()
    {
        string markdown = ReportWriter.Markdown(Report(Task("a", "dev", Record(Outcome.Solved))));

        Assert.DoesNotContain("## 失败", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## 无效", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## 提示", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_UsesCamelCaseAndKeepsChineseReadable()
    {
        using JsonDocument json = JsonDocument.Parse(ReportWriter.Json(Report(new TaskRecord("a", "dev", null, ["标准调用没有取到值"], []))));

        JsonElement task = json.RootElement.GetProperty("tasks")[0];
        Assert.Equal("dev", task.GetProperty("set").GetString());
        Assert.Contains("\"标准调用没有取到值\"", ReportWriter.Json(Report(new TaskRecord("a", "dev", null, ["标准调用没有取到值"], []))), StringComparison.Ordinal);
        Assert.Equal("claude-sonnet-5-5", json.RootElement.GetProperty("model").GetString());
    }

    private static RunReport Report(params TaskRecord[] tasks) =>
        new(new DateTimeOffset(2026, 10, 19, 16, 40, 0, TimeSpan.FromHours(8)), ClaudeCommand.DefaultModel, "claude.exe", "eplanmcp.exe", [], tasks);

    private static TaskRecord Task(string id, string set, params AttemptRecord[] attempts) =>
        new(id, set, null, [], [.. attempts.Select((a, i) => a with { Attempt = i + 1 })]);

    private static AttemptRecord Record(string outcome, int calls = 3, double seconds = 12, decimal cost = 0.1m, string? reason = null, string[]? failures = null) =>
        new(1, outcome, reason, failures ?? [], new Dictionary<string, int> { ["eplan_page_list"] = calls }, 0, 0, 1000, 100, cost, 3, seconds);

    private static AgentAttempt Attempt(AttemptStatus status, params string[] lines)
    {
        var trace = new AgentTrace();
        trace.Read(Init);
        foreach (string line in lines)
        {
            trace.Read(line);
        }

        return new AgentAttempt(status, status == AttemptStatus.Answered ? null : "why", trace.Result?.Answer, trace, TimeSpan.FromSeconds(41.26));
    }

    private static string Use(string id, string tool) =>
        """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"ID","name":"mcp__eplan__TOOL","input":{}}]},"parent_tool_use_id":null}"""
            .Replace("ID", id, StringComparison.Ordinal).Replace("TOOL", tool, StringComparison.Ordinal);

    private static string ToolResult(string id, bool error) =>
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"ID","content":"x","is_error":ERROR}]},"parent_tool_use_id":null}"""
            .Replace("ID", id, StringComparison.Ordinal).Replace("ERROR", error ? "true" : "false", StringComparison.Ordinal);
}

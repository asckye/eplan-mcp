using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Scoring;

[Trait("Size", "Small")]
public sealed class GraderTests
{
    private static readonly EvalTask _task = TaskLoader.Load("device-pages", """
        id: device-pages
        set: dev
        prompt: =GAA+A1-FC1 的触点在哪几页？共几页？
        answer: { type: object, properties: { pages: { type: array }, count: { type: integer }, done: { type: boolean } } }
        oracle:
          pages: { tool: eplan_device_get, args: { tag: "=GAA+A1-FC1" }, pick: "$.functions[*].page" }
        checks:
          - { answer: $.pages, set_equals: oracle.pages }
          - { answer: $.count, at_most: 3 }
          - { answer: $.done, equals: true }
        """).Task!;

    private static readonly Dictionary<string, JsonNode?> _oracle = new() { ["pages"] = JsonNode.Parse("""["/3", "/7"]""") };

    [Fact]
    public void Grade_ACorrectAnswer_IsSolved()
    {
        Verdict verdict = Grader.Grade(_task, JsonNode.Parse("""{"pages": ["/7", "/3", "/3"], "count": 2, "done": true}"""), _oracle);

        Assert.True(verdict.Solved);
        Assert.Empty(verdict.Failures);
        Assert.Empty(verdict.Invalid);
    }

    [Fact]
    public void Grade_NamesEachFailedCheck()
    {
        Verdict verdict = Grader.Grade(_task, JsonNode.Parse("""{"pages": ["/7", "/9"], "count": 4, "done": true}"""), _oracle);

        Assert.False(verdict.Solved);
        Assert.Equal(["$.pages set_equals: missing \"/3\"; unexpected \"/9\"", "$.count at_most: 4 is above 3"], verdict.Failures);
        Assert.Empty(verdict.Invalid);
    }

    [Fact]
    public void Grade_AMissingValue_FailsItsCheck()
    {
        Assert.Equal(
            ["$.count at_most: the answer has no value here", "$.done equals: the answer has no value here"],
            Grader.Grade(_task, JsonNode.Parse("""{"pages": ["/3", "/7"]}"""), _oracle).Failures);
    }

    [Fact]
    public void Grade_NoStructuredAnswer_FailsEveryCheck()
    {
        Assert.Equal(3, Grader.Grade(_task, null, _oracle).Failures.Count);
    }

    [Fact]
    public void Grade_TakesTheExpectedValueFromTheOracle()
    {
        var other = new Dictionary<string, JsonNode?> { ["pages"] = JsonNode.Parse("""["/9"]""") };

        Assert.True(Grader.Grade(_task, JsonNode.Parse("""{"pages": ["/9"], "count": 1, "done": true}"""), other).Solved);
    }

    [Fact]
    public void Grade_AfterChecks_LookAtWhatTheAfterCallsFound()
    {
        EvalTask task = TaskLoader.Load("rename", """
            id: rename
            set: dev
            fixture: copy
            prompt: 在 {project} 中把 =F+A2-KF1 改名为 =F+A2-KF19
            answer: { type: object, properties: { done: { type: boolean } } }
            oracle: { parts: { tool: eplan_device_get, args: { tag: "=F+A2-KF1" }, pick: "$.parts[*].partNumber" } }
            after:
              tags: { tool: eplan_device_get, args: { tag: "=F+A2-KF19" }, pick: "$.functions[*].tag" }
              parts: { tool: eplan_device_get, args: { tag: "=F+A2-KF19" }, pick: "$.parts[*].partNumber" }
              old: { tool: eplan_device_get, args: { tag: "=F+A2-KF1" }, pick: $.error.reason }
            checks:
              - { answer: $.done, equals: true }
              - { after: tags, set_equals: ["=F+A2-KF19"] }
              - { after: parts, set_equals: oracle.parts }
              - { after: old, equals: DEVICE_NOT_FOUND }
            """).Task!;
        var oracle = new Dictionary<string, JsonNode?> { ["parts"] = JsonNode.Parse("""["SIE.3RH2122-1HB40"]""") };
        var after = new Dictionary<string, AfterValue>
        {
            ["tags"] = new(JsonNode.Parse("""["=F+A2-KF19", "=F+A2-KF1"]"""), null),
            ["parts"] = new(JsonNode.Parse("""["SIE.3RH2122-1HB40"]"""), null),
            ["old"] = new(null, "$.error.reason matches nothing in the result of eplan_device_get: {\"project\":\"P\"}"),
        };

        Verdict verdict = Grader.Grade(task, JsonNode.Parse("""{"done": true}"""), oracle, after);

        Assert.Equal(
            [
                "after.tags set_equals: unexpected \"=F+A2-KF1\"",
                "after.old equals: $.error.reason matches nothing in the result of eplan_device_get: {\"project\":\"P\"}",
            ],
            verdict.Failures);
        Assert.Equal(["after.tags set_equals: the after call was not made", "after.parts set_equals: the after call was not made", "after.old equals: the after call was not made"], Grader.Grade(task, JsonNode.Parse("""{"done": true}"""), oracle).Failures);
    }

    [Fact]
    public void Grade_AnExpectedAfterValue_IsWhatTheAfterCallFound()
    {
        EvalTask task = TaskLoader.Load("workflow", """
            id: workflow
            set: dev
            fixture: copy
            prompt: 检查 {project}，导出 PDF，报告错误数和文件名
            answer: { type: object, properties: { errors: { type: integer }, file: { type: string } } }
            after:
              errors: { tool: eplan_check_list, pick: $.counts.error }
              pdf: { tool: exported_files, args: { extension: pdf }, pick: "$.files[0].name" }
              names: { tool: exported_files, pick: "$.files[*].name" }
            checks:
              - { answer: $.errors, equals: after.errors }
              - { answer: $.file, contains: after.pdf }
              - { after: names, contains: after.pdf }
            """).Task!;
        var after = new Dictionary<string, AfterValue>
        {
            ["errors"] = new(JsonValue.Create(2), null),
            ["pdf"] = new(JsonValue.Create("Eval_r1_01.pdf"), null),
            ["names"] = new(JsonNode.Parse("""["Eval_r1_01.pdf"]"""), null),
        };
        var noPdf = new Dictionary<string, AfterValue>(after)
        {
            ["pdf"] = new(null, "$.files[0].name matches nothing in exported_files, which found 0 of the attempt's files"),
            ["names"] = new(JsonNode.Parse("[]"), null),
        };
        var oracle = new Dictionary<string, JsonNode?>();

        Assert.True(Grader.Grade(task, JsonNode.Parse("""{"errors": 2, "file": "C:\\exports\\Eval_r1_01\\x\\Eval_r1_01.pdf"}"""), oracle, after).Solved);
        Assert.Equal(["$.errors equals: 3 is not 2"], Grader.Grade(task, JsonNode.Parse("""{"errors": 3, "file": "Eval_r1_01.pdf"}"""), oracle, after).Failures);
        Assert.Equal(
            [
                "$.file contains: the expected after.pdf: $.files[0].name matches nothing in exported_files, which found 0 of the attempt's files",
                "after.names contains: the expected after.pdf: $.files[0].name matches nothing in exported_files, which found 0 of the attempt's files",
            ],
            Grader.Grade(task, JsonNode.Parse("""{"errors": 2, "file": "Eval_r1_01.pdf"}"""), oracle, noPdf).Failures);
    }

    [Fact]
    public void Grade_AnExpectedAfterValueTheComparisonCannotUse_FailsTheCheck()
    {
        EvalTask task = TaskLoader.Load("t", """
            id: t
            set: dev
            prompt: p
            answer: { type: object, properties: { n: { type: integer } } }
            after: { total: { tool: eplan_check_list, pick: $.total } }
            checks: [{ answer: $.n, at_least: after.total }]
            """).Task!;
        var after = new Dictionary<string, AfterValue> { ["total"] = new(JsonValue.Create("many"), null) };

        Verdict verdict = Grader.Grade(task, JsonNode.Parse("""{"n": 2}"""), new Dictionary<string, JsonNode?>(), after);

        Assert.Equal(["$.n at_least: after.total gave \"many\", but at_least compares with a number"], verdict.Failures);
        Assert.Empty(verdict.Invalid);
    }

    [Fact]
    public void Grade_AnOracleValueTheComparisonCannotUse_MakesTheTaskInvalidNotFailed()
    {
        var scalar = new Dictionary<string, JsonNode?> { ["pages"] = JsonValue.Create(2) };

        Verdict verdict = Grader.Grade(_task, JsonNode.Parse("""{"pages": ["/3", "/7"], "count": 2, "done": true}"""), scalar);

        Assert.False(verdict.Solved);
        Assert.Empty(verdict.Failures);
        Assert.Equal(["$.pages set_equals: oracle 'pages' gave 2, but set_equals compares with a list"], verdict.Invalid);
    }
}

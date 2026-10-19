using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;
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
    public void Grade_AnOracleValueTheComparisonCannotUse_MakesTheTaskInvalidNotFailed()
    {
        var scalar = new Dictionary<string, JsonNode?> { ["pages"] = JsonValue.Create(2) };

        Verdict verdict = Grader.Grade(_task, JsonNode.Parse("""{"pages": ["/3", "/7"], "count": 2, "done": true}"""), scalar);

        Assert.False(verdict.Solved);
        Assert.Empty(verdict.Failures);
        Assert.Equal(["$.pages set_equals: oracle 'pages' gave 2, but set_equals compares with a list"], verdict.Invalid);
    }
}

using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Tasks;

[Trait("Size", "Small")]
public sealed class TaskLoaderTests
{
    // The example of agent evals design 4.1.
    private const string DevicePages = """
        id: device-pages
        set: dev
        prompt: |
          =GAA+A1-FC1 这个设备的触点分布在哪几页？
        answer:
          type: object
          required: [pages]
          properties:
            pages: { type: array, items: { type: string } }
        oracle:
          pages:
            tool: eplan_device_get
            args: { tag: "=GAA+A1-FC1" }
            pick: $.functions[*].page
        checks:
          - answer: $.pages
            set_equals: oracle.pages
        budget: { max_tool_calls: 12, max_minutes: 4 }
        """;

    [Fact]
    public void Load_TheDesignExample_ReadsEveryPart()
    {
        EvalTask task = Valid("device-pages", DevicePages);

        Assert.Equal("device-pages", task.Id);
        Assert.Equal(TaskSet.Dev, task.Set);
        Assert.Equal("=GAA+A1-FC1 这个设备的触点分布在哪几页？", task.Prompt);
        Assert.Equal("""{"type":"object","required":["pages"],"properties":{"pages":{"type":"array","items":{"type":"string"}}}}""", task.Answer.ToJsonString());
        OracleCall pages = Assert.Single(task.Oracle).Value;
        Assert.Equal("eplan_device_get", pages.Tool);
        Assert.Equal("""{"tag":"=GAA+A1-FC1"}""", Comparison.Show(pages.Args));
        Assert.Equal("$.functions[*].page", pages.Pick.Text);
        Check check = Assert.Single(task.Checks);
        Assert.Equal(("$.pages", ComparisonKind.SetEqual, "pages"), (check.Answer.Text, check.Kind, check.Oracle));
        Assert.Equal(new Budget(12, TimeSpan.FromMinutes(4), Budget.Default.MaxUsd), task.Budget);
    }

    [Fact]
    public void Load_ATaskWithLiteralsOnly_NeedsNoOracleAndUsesTheDefaultBudget()
    {
        EvalTask task = Valid("rename-refused", """
            id: rename-refused
            set: holdout
            prompt: 把 -FC1 改名为 -FC9
            answer: { type: object, properties: { done: { type: boolean } } }
            checks:
              - { answer: $.done, equals: false }
            """);

        Assert.Empty(task.Oracle);
        Assert.Equal(TaskSet.Holdout, task.Set);
        Assert.Equal(Budget.Default, task.Budget);
        Assert.Equal("false", Comparison.Show(Assert.Single(task.Checks).Literal));
        Assert.Null(task.Checks[0].Oracle);
    }

    [Fact]
    public void Load_AnOracleCallWithoutArgs_HasEmptyArgs()
    {
        EvalTask task = Valid("x", """
            id: x
            set: dev
            prompt: p
            answer: { type: object }
            oracle: { total: { tool: eplan_page_list, pick: $.total } }
            checks: [{ answer: $.total, equals: oracle.total }]
            """);

        Assert.Empty(task.Oracle["total"].Args);
    }

    [Theory]
    [InlineData("12", "12")]
    [InlineData("'12'", "\"12\"")]
    [InlineData("=GAA+A1-FC1", "\"=GAA+A1-FC1\"")]
    [InlineData("[a, 2, { b: c }]", "[\"a\",2,{\"b\":\"c\"}]")]
    [InlineData("\"oracle.pages\"", "\"oracle.pages\"")]
    [InlineData("null", "null")]
    public void Load_Literals_FollowYamlScalarTyping(string yaml, string json)
    {
        EvalTask task = Valid("t", $$"""
            id: t
            set: dev
            prompt: p
            answer: { type: object }
            checks:
              - { answer: $.value, equals: {{yaml}} }
            """);

        Assert.Equal(json, Comparison.Show(task.Checks[0].Literal));
        Assert.Null(task.Checks[0].Oracle);
    }

    [Fact]
    public void Load_AFileFullOfMistakes_ReportsEveryOneWithItsLine()
    {
        TaskFile file = TaskLoader.Load("device-pages", """
            id: device-page
            set: test
            prompt: "  "
            answer: { type: array }
            oracle:
              Pages:
                tool: device_get
                args: [1]
                pick: functions[*]
                extra: 1
              unused: { tool: eplan_page_list, pick: $.total }
            checks:
              - answer: $.page
                set_equals: oracle.pages
              - answer: $
                equals: 1
                contains: 1
              - { answer: $.total, at_least: many }
              - { answer: $.name, equals_ignore_case: 3 }
              - { answer: $.items, set_equals: a }
              - just text
            budget: { max_tool_calls: 2.5, max_minutes: 0, max_usd: 50, max_tokens: 1 }
            notes: hi
            """);

        Assert.Null(file.Task);
        Assert.Equal(
            [
                "line 23: unknown key notes; expected id, set, prompt, answer, oracle, checks, budget",
                "line 1: id 'device-page' differs from the file name 'device-pages'",
                "line 2: set 'test' is neither dev nor holdout",
                "line 3: 'prompt' must be a non-empty string",
                "line 4: 'answer' must be a JSON Schema of type object",
                "line 10: unknown key extra; expected tool, args, pick",
                "line 7: 'device_get' is not a tool name (eplan_<family>_<verb>)",
                "line 8: 'args' must be a mapping",
                "line 9: 'functions[*]' is not a path of the supported subset ($, .field, [n], [*])",
                "line 14: oracle 'pages' is not defined",
                "line 15: a check needs exactly one of equals, equals_ignore_case, set_equals, contains, at_least, at_most",
                "line 18: at_least compares with a number",
                "line 19: equals_ignore_case compares with a string",
                "line 20: set_equals compares with a list",
                "line 21: a check must be a mapping",
                "line 22: unknown key max_tokens; expected max_tool_calls, max_minutes, max_usd",
                "line 22: 'max_tool_calls' must be a positive whole number up to 200",
                "line 22: 'max_minutes' must be a positive number up to 60",
                "line 22: 'max_usd' must be a positive number up to 5",
                "line 6: oracle 'Pages' is not used by any check",
                "line 11: oracle 'unused' is not used by any check",
            ],
            file.Problems);
    }

    [Fact]
    public void Load_AStringSplitByACommaInsideBraces_IsReportedAsAKeyWithoutValue()
    {
        // The first real run: the description became two keys, and claude rejected the schema.
        TaskFile file = TaskLoader.Load("t", """
            id: t
            set: dev
            prompt: p
            answer: { type: object, properties: { device: { type: string, description: the full tag, such as =GAA+A1-FC9 } } }
            checks: [{ answer: $.device, equals: "=GAA+A1-FC2" }]
            """);

        Assert.Equal(["line 4: 'such as =GAA+A1-FC9' in 'answer' has no value; quote a string that contains ', ' inside { }"], file.Problems);
    }

    [Fact]
    public void Load_AnExplicitNullInTheAnswerSchema_Passes()
    {
        EvalTask task = Valid("t", """
            id: t
            set: dev
            prompt: p
            answer: { type: object, properties: { device: { type: [string, "null"], default: null } } }
            checks: [{ answer: $.device, equals: null }]
            """);

        Assert.Equal("t", task.Id);
    }

    [Fact]
    public void Load_ACheckPathOutsideTheAnswerSchema_IsReported()
    {
        TaskFile file = TaskLoader.Load("t", """
            id: t
            set: dev
            prompt: p
            answer: { type: object, properties: { pages: { type: array } } }
            checks:
              - { answer: $.page, contains: "/3" }
              - { answer: $, contains: "/3" }
            """);

        Assert.Equal(["line 6: the answer schema has no property 'page'"], file.Problems);
    }

    [Fact]
    public void Load_MissingKeys_AreReportedAtTheMapping()
    {
        TaskFile file = TaskLoader.Load("t", """
            set: dev
            oracle: { a: { args: {} } }
            """);

        Assert.Equal(
            [
                "line 1: 'id' is missing",
                "line 1: 'prompt' is missing",
                "line 1: 'answer' is missing",
                "line 2: 'tool' is missing",
                "line 2: 'pick' is missing",
                "line 1: 'checks' is missing",
                "line 2: oracle 'a' is not used by any check",
            ],
            file.Problems);
    }

    [Theory]
    [InlineData("id: t\nid: t\n", "Duplicate key")]
    [InlineData("id: [t\n", "line 2: While parsing a flow sequence, did not find expected ',' or ']'.")]
    [InlineData("- id: t\n", "a task file holds exactly one YAML mapping")]
    [InlineData("id: t\n---\nid: u\n", "a task file holds exactly one YAML mapping")]
    [InlineData("", "a task file holds exactly one YAML mapping")]
    public void Load_AFileThatIsNotOneMapping_IsReported(string yaml, string problem)
    {
        TaskFile file = TaskLoader.Load("t", yaml);

        Assert.Null(file.Task);
        Assert.Contains(problem, Assert.Single(file.Problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("- { answer: $.n, equals: }", "line 6: equals has no expected value; write null to compare with null")]
    [InlineData("- { answer: $.n, contains: [] }", "line 6: contains needs something to look for, not an empty list")]
    public void Load_ExpectedValuesThatCannotBeCompared_AreReported(string check, string problem)
    {
        TaskFile file = TaskLoader.Load("t", $"""
            id: t
            set: dev
            prompt: p
            answer: {"{"} type: object {"}"}
            checks:
              {check}
            """);

        Assert.Equal([problem], file.Problems);
    }

    private static EvalTask Valid(string name, string yaml)
    {
        TaskFile file = TaskLoader.Load(name, yaml);
        Assert.Empty(file.Problems);
        return file.Task!;
    }
}

using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Server;

[Trait("Size", "Small")]
public sealed class OracleRunnerTests
{
    private static readonly HashSet<string> _tools = ["eplan_page_list", "eplan_device_get"];

    private static readonly EvalTask _task = TaskLoader.Load("t", """
        id: t
        set: dev
        prompt: =GAA+A1 下有哪些多线原理图页？共几页？
        answer: { type: object, properties: { pages: { type: array }, total: { type: integer } } }
        oracle:
          pages: { tool: eplan_page_list, args: { structure: "=GAA+A1", page_type: circuit }, pick: "$.pages[*].name" }
          total: { tool: eplan_page_list, args: { structure: "=GAA+A1", page_type: circuit }, pick: $.total }
        checks:
          - { answer: $.pages, set_equals: oracle.pages }
          - { answer: $.total, equals: oracle.total }
        """).Task!;

    [Fact]
    public async Task Run_AWildcardPick_CollectsEveryPageAndKeepsTheArguments()
    {
        FakeToolClient client = TwoPages(new FakeToolClient());

        OracleValues values = await OracleRunner.RunAsync(_task, client, _tools, TestContext.Current.CancellationToken);

        Assert.Empty(values.Problems);
        Assert.Equal("""["/1","/2","/3"]""", Comparison.Show(values.Values["pages"]));
        Assert.Equal("3", Comparison.Show(values.Values["total"]));
        Assert.Equal(
            [
                """eplan_page_list {"structure":"=GAA+A1","page_type":"circuit"}""",
                """eplan_page_list {"structure":"=GAA+A1","page_type":"circuit","cursor":"c2"}""",
            ],
            client.Calls);
    }

    [Fact]
    public async Task Run_AToolTheServerLacks_MakesTheTaskInvalid()
    {
        OracleValues values = await OracleRunner.RunAsync(_task, TwoPages(new FakeToolClient()), new HashSet<string> { "eplan_device_get" }, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["oracle 'pages': eplan_page_list is not a tool of this eplanmcp", "oracle 'total': eplan_page_list is not a tool of this eplanmcp"],
            values.Problems);
    }

    [Fact]
    public async Task Run_AFailedCall_MakesTheTaskInvalidWithTheToolsText()
    {
        FakeToolClient client = new FakeToolClient().Fail("eplan_page_list", "EPLAN_NOT_CONNECTED: start EPLAN");

        OracleValues values = await OracleRunner.RunAsync(_task, client, _tools, TestContext.Current.CancellationToken);

        // Both oracle entries share the one call.
        Assert.Equal(
            ["oracle 'pages': eplan_page_list failed: EPLAN_NOT_CONNECTED: start EPLAN", "oracle 'total': eplan_page_list failed: EPLAN_NOT_CONNECTED: start EPLAN"],
            values.Problems);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task Run_ADefinitePickThatMatchesNothing_MakesTheTaskInvalid()
    {
        EvalTask task = TaskLoader.Load("t", """
            id: t
            set: dev
            prompt: p
            answer: { type: object }
            oracle: { tag: { tool: eplan_device_get, args: { tag: "-FC1" }, pick: $.device.tag } }
            checks: [{ answer: $.tag, equals: oracle.tag }]
            """).Task!;

        OracleValues values = await OracleRunner.RunAsync(task, new FakeToolClient().Answer("eplan_device_get", """{"tag":"=GAA+A1-FC1"}"""), _tools, TestContext.Current.CancellationToken);

        Assert.Equal(["oracle 'tag': $.device.tag matches nothing in the result of eplan_device_get"], values.Problems);
    }

    [Fact]
    public async Task Run_AWildcardPickThatMatchesNothing_IsANote()
    {
        FakeToolClient client = new FakeToolClient().Answer("eplan_page_list", """{"pages":[],"total":0}""");

        OracleValues values = await OracleRunner.RunAsync(_task, client, _tools, TestContext.Current.CancellationToken);

        Assert.Empty(values.Problems);
        Assert.Equal("[]", Comparison.Show(values.Values["pages"]));
        Assert.StartsWith("oracle 'pages': $.pages[*].name matched nothing", Assert.Single(values.Notes), StringComparison.Ordinal);
    }

    /// <summary>The answers to one oracle call whose result spans two pages.</summary>
    private static FakeToolClient TwoPages(FakeToolClient client) => client
        .Answer("eplan_page_list", """{"pages":[{"name":"/1"},{"name":"/2"}],"total":3,"nextCursor":"c2","truncated":true}""")
        .Answer("eplan_page_list", """{"pages":[{"name":"/3"}],"total":3,"nextCursor":null}""");
}

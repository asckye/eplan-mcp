using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Tests.Server;

[Trait("Size", "Small")]
public sealed class AfterRunnerTests
{
    private const string Copy = @"C:\scratch\evals\r1\copies\01\Eval_r1_01";

    private static readonly EvalTask _task = TaskLoader.Load("rename", """
        id: rename
        set: dev
        fixture: copy
        prompt: 在 {project} 中把 =F+A2-KF1 改名为 =F+A2-KF19
        answer: { type: object }
        after:
          tags: { tool: eplan_device_get, args: { tag: "=F+A2-KF19" }, pick: "$.functions[*].tag" }
          old: { tool: eplan_device_get, args: { tag: "=F+A2-KF1" }, pick: $.error.reason }
          fixture: { tool: eplan_device_get, args: { tag: "=F+A2-KF1", project: EPLAN_Sample_Project }, pick: $.device.tag }
        checks:
          - { after: tags, set_equals: ["=F+A2-KF19"] }
          - { after: old, equals: DEVICE_NOT_FOUND }
          - { after: fixture, equals: "=F+A2-KF1" }
        """).Task!;

    [Fact]
    public async Task Run_PicksFromResultsAndErrorsOfCallsOnTheCopy()
    {
        FakeToolClient client = new FakeToolClient()
            .Answer("eplan_device_get", """{"device":{"tag":"=F+A2-KF19"},"functions":[{"tag":"=F+A2-KF19"},{"tag":"=F+A2-KF19"}]}""")
            .Fail("eplan_device_get", """{"error":{"reason":"DEVICE_NOT_FOUND","execution":null}}""")
            .Answer("eplan_device_get", """{"device":{"tag":"=F+A2-KF1"}}""");

        IReadOnlyDictionary<string, AfterValue> values = await AfterRunner.RunAsync(_task, client, Copy, TestContext.Current.CancellationToken);

        Assert.Equal("""["=F+A2-KF19","=F+A2-KF19"]""", Comparison.Show(values["tags"].Value));
        Assert.Equal("\"DEVICE_NOT_FOUND\"", Comparison.Show(values["old"].Value));
        Assert.All(values.Values, v => Assert.Null(v.Missing));
        Assert.Equal(
            [
                """eplan_device_get {"tag":"=F+A2-KF19","project":"C:\\scratch\\evals\\r1\\copies\\01\\Eval_r1_01"}""",
                """eplan_device_get {"tag":"=F+A2-KF1","project":"C:\\scratch\\evals\\r1\\copies\\01\\Eval_r1_01"}""",
                """eplan_device_get {"tag":"=F+A2-KF1","project":"EPLAN_Sample_Project"}""",
            ],
            client.Calls);
    }

    [Fact]
    public async Task Run_APickThatMatchesNothingOrTextThatIsNoJson_IsMissingWithTheToolsText()
    {
        FakeToolClient client = new FakeToolClient()
            .Answer("eplan_device_get", """{"device":{"tag":"=F+A2-KF19"}}""")
            .Answer("eplan_device_get", """{"device":{"tag":"=F+A2-KF1"}}""")
            .Fail("eplan_device_get", "EPLAN is gone");

        IReadOnlyDictionary<string, AfterValue> values = await AfterRunner.RunAsync(_task, client, Copy, TestContext.Current.CancellationToken);

        Assert.Equal("[]", Comparison.Show(values["tags"].Value));
        Assert.Equal((null, "$.error.reason matches nothing in the result of eplan_device_get: ok"), (values["old"].Value, values["old"].Missing));
        Assert.Equal("eplan_device_get returned nothing to pick from: EPLAN is gone", values["fixture"].Missing);
    }
}

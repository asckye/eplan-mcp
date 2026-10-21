using EplanMcp.Evals.Agent;
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

        IReadOnlyDictionary<string, AfterValue> values = await AfterRunner.RunAsync(_task, client, Copy, [], TestContext.Current.CancellationToken);

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

        IReadOnlyDictionary<string, AfterValue> values = await AfterRunner.RunAsync(_task, client, Copy, [], TestContext.Current.CancellationToken);

        Assert.Equal("[]", Comparison.Show(values["tags"].Value));
        Assert.Equal((null, "$.error.reason matches nothing in the result of eplan_device_get: ok"), (values["old"].Value, values["old"].Missing));
        Assert.Equal("eplan_device_get returned nothing to pick from: EPLAN is gone", values["fixture"].Missing);
    }

    [Fact]
    public async Task Run_ExportedFiles_PicksFromTheCopysExportsWithoutCallingATool()
    {
        EvalTask task = TaskLoader.Load("export", """
            id: export
            set: dev
            fixture: copy
            prompt: 把 {project} 的 =GAA+A1&EFS1/60 导出成 PDF
            answer: { type: object }
            after:
              pdfs: { tool: exported_files, args: { extension: pdf }, pick: $.count }
              pdf: { tool: exported_files, args: { extension: .PDF }, pick: "$.files[0].name" }
              all: { tool: exported_files, pick: "$.files[*].path" }
              second: { tool: exported_files, args: { extension: pdf }, pick: "$.files[1].name" }
            checks:
              - { after: pdfs, equals: 1 }
              - { after: pdf, equals: Eval_r1_01.pdf }
              - { after: all, contains: x }
              - { after: second, equals: x }
            """).Task!;
        ExportedFile[] exports =
        [
            new(@"EPLAN_Sample_Project\20261021T083000Z-aaaaaa\EPLAN_Sample_Project.pdf", 7),
            new(@"Eval_r1_01\20261021T083100Z-bbbbbb\Eval_r1_01.pdf", 2048),
            new(@"Eval_r1_01\20261021T083200Z-cccccc\=GAA+A1&EFS1_60.png", 512),
        ];
        var client = new FakeToolClient();

        IReadOnlyDictionary<string, AfterValue> values = await AfterRunner.RunAsync(task, client, Copy, exports, TestContext.Current.CancellationToken);

        Assert.Equal("1", Comparison.Show(values["pdfs"].Value));
        Assert.Equal("\"Eval_r1_01.pdf\"", Comparison.Show(values["pdf"].Value));
        Assert.Equal("""["Eval_r1_01\\20261021T083100Z-bbbbbb\\Eval_r1_01.pdf","Eval_r1_01\\20261021T083200Z-cccccc\\=GAA+A1&EFS1_60.png"]""", Comparison.Show(values["all"].Value));
        Assert.Equal("$.files[1].name matches nothing in exported_files, which found 1 of the attempt's files", values["second"].Missing);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public void Exported_OnTheSharedFixture_ListsEveryExportOfTheAttempt()
    {
        ExportedFile[] exports = [new(@"EPLAN_Sample_Project\20261021T083000Z-aaaaaa\EPLAN_Sample_Project.pdf", 7)];

        Assert.Equal(
            """{"files":[{"path":"EPLAN_Sample_Project\\20261021T083000Z-aaaaaa\\EPLAN_Sample_Project.pdf","name":"EPLAN_Sample_Project.pdf","bytes":7}],"count":1}""",
            Comparison.Show(AfterRunner.Exported(exports, null, new())));
        Assert.Equal("""{"files":[],"count":0}""", Comparison.Show(AfterRunner.Exported(exports, null, new() { ["extension"] = "dxf" })));
    }
}

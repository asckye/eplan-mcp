using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_page_renumber through the real write pipeline against a fake Bridge (docs/tools/eplan_page_renumber.md,
/// sections 3, 4, 6 and 7), with what spike S12 found EPLAN does to /90-/92 from 200 in steps of 10 (api-notes A18).
/// </summary>
[Trait("Size", "Medium")]
public sealed class PageRenumberToolTests : IDisposable
{
    private static readonly string[] _pages = ["=GAA+A1&EFS1/90", "=GAA+A1&EFS1/91", "=GAA+A1&EFS1/92"];

    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly WriteToolHarness _harness;

    public PageRenumberToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.PageRenumberPlan] = _ =>
        {
            WritePlan plan = _harness.Plan(_pages.Select(p => new PlannedChange { Action = "update", Kind = "page", Name = p, Property = "page number" }));
            plan.Details = new Dictionary<string, string> { ["numbering"] = "from 200, step 10, per structure, subpages: retain, keep text: no" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.PageRenumberExecute] = args =>
        {
            File.WriteAllText(((PageRenumberArgs)args!).Backup!.Path, "copy");
            return new PageRenumberData
            {
                Project = "Line1",
                Changed = [.. _pages.Select((p, i) => new RenamedObject { From = p, To = "=GAA+A1&EFS1/" + (200 + (10 * i)).ToString(System.Globalization.CultureInfo.InvariantCulture) })],
                Unchanged = 0,
            };
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ShowsTheNumberingOnly()
    {
        CallToolResult result = await Renumber(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        JsonElement scope = data.GetProperty("scope");
        Assert.Equal((200, 10, false, "retain"), (scope.GetProperty("start").GetInt32(), scope.GetProperty("step").GetInt32(), scope.GetProperty("keepText").GetBoolean(), scope.GetProperty("subpages").GetString()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("changed").ValueKind, data.GetProperty("unchanged").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("Dry run: would renumber 3 pages from 200 in steps of 10; the new names are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenEplanRenumbersAfterABackup()
    {
        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal(["renumber page =GAA+A1&EFS1/90", "renumber page =GAA+A1&EFS1/91", "renumber page =GAA+A1&EFS1/92"], question.Changes);
        var sent = (PageRenumberArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", 200, 10, false, "retain", 33), (sent.Fingerprint, sent.Start, sent.Step, sent.KeepText, sent.Subpages, sent.Backup!.MaxSeconds));
        Assert.Equal(("committed", 0, "AI: eplan_page_renumber", sent.Backup.Path), (data.GetProperty("execution").GetString(), data.GetProperty("unchanged").GetInt32(), data.GetProperty("undo").GetString(), data.GetProperty("backup").GetString()));
        Assert.Equal(["=GAA+A1&EFS1/200", "=GAA+A1&EFS1/210", "=GAA+A1&EFS1/220"], data.GetProperty("changed").EnumerateArray().Select(c => c.GetProperty("to").GetString()));
        Assert.Equal("Reports such as the table of contents may need eplan_report_update.", data.GetProperty("hint").GetString());
        Assert.Equal("Renumbered 3 pages. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task ANumberInUse_IsRolledBackWithTheNames()
    {
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = ErrorReason.AlreadyExists,
            Message = "m",
            Execution = ExecutionState.RolledBack,
            Metadata = new Dictionary<string, string> { ["existing"] = "=GAA+A1&EFS1/1" },
        };

        JsonElement error = WriteToolHarness.Error(await Renumber());

        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.RolledBack, "=GAA+A1&EFS1/1"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("existing").GetString()));
        Assert.True(error.GetProperty("metadata").TryGetProperty("backup", out _));
    }

    [Theory]
    [InlineData(0, 1, "'start' and 'step' must be at least 1.")]
    [InlineData(200, 0, "'start' and 'step' must be at least 1.")]
    public async Task ArgumentProblems_AreRefusedBeforeEplanIsAsked(int start, int step, string violation)
    {
        JsonElement error = WriteToolHarness.Error(await PageDestructiveTools.PageRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", _pages, start, step, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(violation, error.GetProperty("metadata").GetProperty("violations").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Renumber(bool dryRun = false) =>
        PageDestructiveTools.PageRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", _pages, 200, 10, dry_run: dryRun, cancellationToken: TestContext.Current.CancellationToken);
}

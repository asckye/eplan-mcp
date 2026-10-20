using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_page_delete through the real write pipeline against a fake Bridge (docs/tools/eplan_page_delete.md, sections
/// 3, 4, 6 and 7): asked in scratch too, backed up before it runs, and S12's page with its four cables (api-notes A18).
/// </summary>
[Trait("Size", "Medium")]
public sealed class PageDeleteToolTests : IDisposable
{
    private const string Page = "=GAA+A1&EFS1/1";

    private readonly FakeApprovalChannel _dialog;
    private readonly WriteToolHarness _harness;
    private int _orphans = 4;
    private ApprovalDecision _decision = ApprovalDecision.Granted;

    public PageDeleteToolTests()
    {
        _dialog = new FakeApprovalChannel("eplan_dialog", null) { Answers = _ => _decision };
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.PageDeletePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "delete", Kind = "page", Name = Page, OldValue = "Circuit, 59 functions" }]);
            plan.Details = new Dictionary<string, string>
            {
                ["pages"] = Page + "\tCircuit\t59",
                ["orphaned"] = string.Join("\n", Tags().Take(50).Select(t => t + "\t6\t=GAA+A1&EFS1/2")),
                ["orphanedCount"] = _orphans.ToString(CultureInfo.InvariantCulture),
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.PageDeleteExecute] = args =>
        {
            File.WriteAllText(((PageDeleteArgs)args!).Backup!.Path, "copy");
            return new PageDeleteData
            {
                Project = "Line1",
                Pages = [new DeletedPage { Name = Page, Type = "Circuit", Functions = 59 }],
                Orphaned = [.. Tags().Take(50).Select(t => new OrphanedDevice { Tag = t, FunctionsLeft = 1, Pages = ["=GAA+A1&EFS1/3"] })],
                OrphanedCount = _orphans,
            };
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ListsThePagesAndTheDevicesLeftBehind_WithoutAskingOrBackingUp()
    {
        CallToolResult result = await Delete(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("backup").ValueKind, data.GetProperty("undo").ValueKind));
        Assert.Equal((Page, "Circuit", 59), Single(data.GetProperty("pages"), p => (p.GetProperty("name").GetString(), p.GetProperty("type").GetString(), p.GetProperty("functions").GetInt32())));
        JsonElement orphan = data.GetProperty("orphaned")[0];
        Assert.Equal(("+A1-WE1", 6, "=GAA+A1&EFS1/2"), (orphan.GetProperty("tag").GetString(), orphan.GetProperty("functionsLeft").GetInt32(), orphan.GetProperty("pages")[0].GetString()));
        Assert.Equal("Reports are not updated when pages are deleted; call eplan_report_update.", data.GetProperty("hint").GetString());
        Assert.Equal("Dry run: would delete 1 page (59 functions); 4 devices would keep functions on other pages without their main function. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenTheProjectIsBackedUpAndThePagesDeleted()
    {
        CallToolResult result = await Delete();

        JsonElement data = WriteToolHarness.Structured(result);
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal(["delete page =GAA+A1&EFS1/1 (Circuit, 59 functions)"], question.Changes);
        var sent = (PageDeleteArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", Page), (sent.Fingerprint, Assert.Single(sent.Pages)));
        Assert.StartsWith(Path.Combine(_harness.Locations.Backups, "Line1") + Path.DirectorySeparatorChar, sent.Backup!.Path, StringComparison.Ordinal);
        Assert.Equal(PageWriteTools.Limit(1).TotalSeconds / 2, sent.Backup.MaxSeconds);
        Assert.Equal(("committed", sent.Backup.Path, "AI: eplan_page_delete"), (data.GetProperty("execution").GetString(), data.GetProperty("backup").GetString(), data.GetProperty("undo").GetString()));
        Assert.Equal("=GAA+A1&EFS1/3", data.GetProperty("orphaned")[0].GetProperty("pages")[0].GetString());
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.Equal("Deleted 1 page; 4 devices keep functions on other pages without their main function. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task MoreThan50DevicesLeftBehind_AreCutAndCounted()
    {
        _orphans = 60;

        JsonElement data = WriteToolHarness.Structured(await Delete());

        Assert.Equal((50, true), (data.GetProperty("orphaned").GetArrayLength(), data.GetProperty("truncated").GetBoolean()));
        Assert.Contains("60 devices lose their main function; only the first 50 are listed.", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_NothingIsBackedUpOrDeleted()
    {
        _decision = ApprovalDecision.Denied;

        JsonElement error = WriteToolHarness.Error(await Delete());

        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task ABackupThatFailed_IsReportedAsNotStarted()
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.BackupFailed, Message = BackupFolders.FailedMessage, Execution = ExecutionState.NotStarted };

        JsonElement error = WriteToolHarness.Error(await Delete());

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, false), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("retryable").GetBoolean()));
        Assert.EndsWith("Z-failed", Assert.Single(Directory.EnumerateDirectories(Path.Combine(_harness.Locations.Backups, "Line1"))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPages_AreReportedFromThePlan()
    {
        _harness.Bridge.Handlers[OperationNames.PageDeletePlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.PageNotFound,
            Message = "m",
            Metadata = new Dictionary<string, string> { ["missing"] = "=gaa+a1&efs1/1" },
        });

        JsonElement error = WriteToolHarness.Error(await Delete());

        Assert.Equal((ErrorReason.PageNotFound, ExecutionState.NotStarted, "=gaa+a1&efs1/1"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("missing").GetString()));
        Assert.Empty(_dialog.Questions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task TooFewOrTooManyPages_AreRefusedBeforeEplanIsAsked(int count)
    {
        string[] pages = [.. Enumerable.Range(1, count).Select(i => "=A/" + i.ToString(CultureInfo.InvariantCulture))];

        JsonElement error = WriteToolHarness.Error(await PageDestructiveTools.PageDeleteAsync(_harness.Pipeline, new RecordedProgress(), "Line1", pages, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("'pages' must hold 1 to 50 full page names.", error.GetProperty("metadata").GetProperty("violations").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private IEnumerable<string> Tags() => ["+A1-WE1", "+A1-WE2", "+A2-WE1", "+A2-WE2", .. Enumerable.Range(5, Math.Max(0, _orphans - 4)).Select(i => "+A3-WE" + i.ToString(CultureInfo.InvariantCulture))];

    private static T Single<T>(JsonElement array, Func<JsonElement, T> select) => select(Assert.Single(array.EnumerateArray()));

    private Task<CallToolResult> Delete(bool dryRun = false) =>
        PageDestructiveTools.PageDeleteAsync(_harness.Pipeline, new RecordedProgress(), "Line1", [Page], dryRun, TestContext.Current.CancellationToken);
}

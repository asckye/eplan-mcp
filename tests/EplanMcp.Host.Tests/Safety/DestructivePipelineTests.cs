using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// The Destructive steps of the write pipeline against a fake Bridge (ADR-0005, ADR-0013, SEC-21): approval in scratch
/// too, then a new backup folder whose copy path goes with the execution, its fate when the write did not run, and the
/// audit fields. Real files for the audit, the suspensions and the backup folders.
/// </summary>
[Trait("Size", "Medium")]
public sealed class DestructivePipelineTests : IDisposable
{
    private const string Tool = "eplan_page_delete";
    private const string Plan = "page.delete.plan";
    private const string Execute = "page.delete.execute";

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-destructive-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private readonly string _scratchProject;
    private BackupTarget? _sent;

    public DestructivePipelineTests()
    {
        _scratchProject = Path.Combine(_locations.DefaultScratch, "Line1");
        _bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", Environment.ProcessId)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3" } },
            null);
        _bridge.Handlers[Plan] = _ => new WritePlan
        {
            Project = "Line1",
            ProjectPath = _scratchProject,
            Changes = [new PlannedChange { Action = "delete", Kind = "page", Name = "=A1&EFS1/5", OldValue = "Circuit, 3 functions" }],
            Fingerprint = "f1",
        };
        _bridge.Handlers[Execute] = args =>
        {
            // The Bridge's copy, as CopyProject leaves it.
            File.WriteAllText(_sent!.Path, "copy");
            return new JsonObject { ["deleted"] = 1 };
        };
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenTheWriteGetsANewBackupFolder()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        WriteOutcome<JsonObject> outcome = await Run(dialog);

        Assert.Equal(ExecutionState.Committed, outcome.Execution);
        Assert.Equal(Path.Combine(_locations.Backups, "Line1"), Assert.Single(dialog.Questions).BackupFolder);
        Assert.StartsWith(Path.Combine(_locations.Backups, "Line1") + Path.DirectorySeparatorChar, _sent!.Path, StringComparison.Ordinal);
        Assert.EndsWith(@"Z\Line1.elk", _sent.Path, StringComparison.Ordinal);
        Assert.Equal((90, _sent.Path), (_sent.MaxSeconds, outcome.Backup));
        List<JsonObject> records = AuditRecords();
        Assert.Equal(["approval", "execution_started", "execution_result"], records.Select(r => r["kind"]!.GetValue<string>()));
        Assert.Equal(_sent.Path, records[1]["backup"]!.GetValue<string>());
        Assert.True(records[2]["backupCompleted"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(nameof(ApprovalDecision.Denied))]
    [InlineData(nameof(ApprovalDecision.Expired))]
    public async Task NotApproved_NoFolderIsMadeAndNothingRuns(string decision)
    {
        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", Enum.Parse<ApprovalDecision>(decision)));

        Assert.Equal(ExecutionState.NotStarted, error.Execution);
        Assert.Empty(_bridge.Writes);
        Assert.False(Directory.Exists(_locations.Backups));
    }

    [Fact]
    public async Task ADryRun_AsksNobodyAndMakesNoFolder()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        WriteOutcome<JsonObject> outcome = await Run(dialog, dryRun: true);

        Assert.Equal((ExecutionState.NotStarted, null), (outcome.Execution, outcome.Backup));
        Assert.Empty(dialog.Questions);
        Assert.False(Directory.Exists(_locations.Backups));
    }

    [Fact]
    public async Task ABackupThatFailed_IsNotStarted_AndItsFolderIsMarkedFailed()
    {
        _bridge.WriteFailure = new BridgeError { Reason = ErrorReason.BackupFailed, Message = BackupFolders.FailedMessage, Execution = ExecutionState.NotStarted };

        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted));

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.EndsWith("Z-failed", Assert.Single(Directory.EnumerateDirectories(Path.Combine(_locations.Backups, "Line1"))), StringComparison.Ordinal);
        Assert.False(AuditRecords()[^1]["backupCompleted"]!.GetValue<bool>());
        Assert.Empty(SuspensionStore.For(_locations).All());
    }

    [Fact]
    public async Task AWriteRefusedBeforeTheBackup_LeavesNoEmptyFolder()
    {
        _bridge.WriteFailure = new BridgeError { Reason = ErrorReason.PlanStale, Message = "m", Execution = ExecutionState.NotStarted };

        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted));

        Assert.Equal(ErrorReason.PlanStale, error.Reason);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_locations.Backups, "Line1")));
    }

    [Fact]
    public async Task ARolledBackWrite_KeepsTheBackupAndNamesIt()
    {
        _bridge.WriteFailure = new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "m", Execution = ExecutionState.RolledBack };

        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted));

        string folder = Assert.Single(Directory.EnumerateDirectories(Path.Combine(_locations.Backups, "Line1")));
        Assert.Equal(Path.Combine(folder, "Line1.elk"), error.Metadata!["backup"]);
    }

    [Fact]
    public async Task WhenNoFolderCanBeMade_TheWriteIsNotSent()
    {
        Directory.CreateDirectory(_locations.Root);
        File.WriteAllText(_locations.Backups, "a file where the backup folder should be");

        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted));

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, BackupFolders.FailedMessage), (error.Reason, error.Execution, error.Message));
        Assert.Empty(_bridge.Writes);
        Assert.Equal(["approval"], AuditRecords().Select(r => r["kind"]!.GetValue<string>()));
    }

    [Fact]
    public async Task AWriteThatIsNotDestructive_GetsNoBackup()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        _bridge.Handlers[Execute] = _ => new JsonObject();

        WriteOutcome<JsonObject> outcome = await Run(dialog, tier: ToolTier.Write);

        Assert.Equal(ExecutionState.Committed, outcome.Execution);
        Assert.Empty(dialog.Questions);
        Assert.Null(_sent);
        Assert.All(AuditRecords(), r => Assert.False(r.ContainsKey("backup") || r.ContainsKey("backupCompleted")));
    }

    private Task<WriteOutcome<JsonObject>> Run(IApprovalChannel dialog, bool dryRun = false, ToolTier tier = ToolTier.Destructive)
    {
        var audit = new AuditLog(_locations.Audit, TimeProvider.System);
        HostConfig config = HostConfig.Defaults(_locations);
        var pipeline = new WritePipeline(
            _bridge,
            config,
            SuspensionStore.For(_locations),
            audit,
            new DanglingWrites(_bridge, SuspensionStore.For(_locations), audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService([dialog], config, TimeProvider.System, NullLogger<ApprovalService>.Instance),
            new BackupFolders(_locations, TimeProvider.System),
            TimeProvider.System,
            NullLogger<WritePipeline>.Instance);
        var request = new WriteRequest<object, object>(
            Tool,
            tier,
            Plan,
            Execute,
            new { pages = 1 },
            (plan, backup) =>
            {
                _sent = backup;
                return new { plan.Fingerprint, backup };
            },
            dryRun,
            TimeSpan.FromSeconds(180));
        return pipeline.RunAsync<object, object, JsonObject>(request, TestContext.Current.CancellationToken);
    }

    private async Task<BridgeError> Fails(IApprovalChannel dialog)
    {
        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run(dialog))).Error;
        Assert.True(ExecutionInvariants.Hold(error), $"{error.Reason}/{error.Execution}/{error.Retryable}");
        return error;
    }

    private List<JsonObject> AuditRecords() =>
        [.. Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).Order(StringComparer.Ordinal).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];
}

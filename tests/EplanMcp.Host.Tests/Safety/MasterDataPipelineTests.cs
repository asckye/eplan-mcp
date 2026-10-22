using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// Writes to system master data through the write pipeline against a fake Bridge (ADR-0019): always approved, with the
/// database named; masterDataWrites and SQL Server refused before anyone is asked; the database backed up into a new
/// folder before the write is sent; a failed backup runs nothing; an unknown outcome suspends the database by its path.
/// Real files for the audit, the suspensions and the backup folders.
/// </summary>
[Trait("Size", "Medium")]
public sealed class MasterDataPipelineTests : IDisposable
{
    private const string Tool = "eplan_part_import";
    private const string Plan = "part.import.plan";
    private const string Execute = "part.import.execute";
    private const string Database = @"$(MD_PARTS)\EPLAN_parts.alk";

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-masterdata-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private readonly List<MasterDataBackupArgs> _backups = [];
    private bool _server;

    public MasterDataPipelineTests()
    {
        _bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", Environment.ProcessId)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3" } },
            null);
        _bridge.Handlers[Plan] = _ => new WritePlan
        {
            Changes = [new PlannedChange { Action = "create", Kind = "part", Name = "added", Property = "parts", NewValue = "3" }],
            Fingerprint = "f1",
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = _server ? "SRV|Parts" : Database, Path = _server ? string.Empty : PartsPath, Server = _server },
        };
        _bridge.Handlers[OperationNames.MasterDataBackup] = args =>
        {
            var backup = (MasterDataBackupArgs)args!;
            _backups.Add(backup);
            string archive = Path.Combine(backup.Folder, backup.ArchiveName + ".zw6");
            File.WriteAllBytes(archive, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]);
            return new MasterDataBackupData { Database = backup.Database, Archive = archive, Bytes = 6 };
        };
        _bridge.Handlers[Execute] = _ => new JsonObject { ["added"] = 3 };
    }

    private string PartsPath => Path.Combine(_locations.Root, "Parts", "EPLAN_parts.alk");

    private string MasterDataBackups => Path.Combine(_locations.Backups, BackupFolders.MasterData);

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public void TheMasterDataBackupFolder_IsBelowTheBackupRoot()
    {
        Assert.Equal(Path.Combine(_locations.Root, "backups", "masterdata"), MasterDataBackups);
    }

    [Fact]
    public async Task TheEngineerIsAsked_ThenTheDatabaseIsBackedUp_ThenTheWriteRuns()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        WriteOutcome<JsonObject> outcome = await Run(dialog);

        ApprovalQuestion question = Assert.Single(dialog.Questions);
        Assert.Equal((MasterDataBackups, null), (question.MasterDataBackupFolder, question.BackupFolder));
        MasterDataBackupArgs backup = Assert.Single(_backups);
        Assert.Equal((MasterDataKinds.Parts, Database, "parts", 90), (backup.Kind, backup.Database, backup.ArchiveName, backup.MaxSeconds));
        Assert.StartsWith(MasterDataBackups + Path.DirectorySeparatorChar, backup.Folder, StringComparison.Ordinal);
        Assert.Equal(ExecutionState.Committed, outcome.Execution);
        Assert.Equal((Path.Combine(backup.Folder, "parts.zw6"), null), (outcome.MasterDataBackup, outcome.Backup));
        Assert.Equal([OperationNames.MasterDataBackup, Execute], _bridge.Calls.Skip(1).Select(c => c.Operation));

        List<JsonObject> records = AuditRecords();
        Assert.Equal(["approval", "execution_started", "execution_result"], records.Select(r => r["kind"]!.GetValue<string>()));
        Assert.All(records, r => Assert.Equal(Database, r["masterData"]!["database"]!.GetValue<string>()));
        Assert.Equal(backup.Folder, records[1]["masterDataBackupFolder"]!.GetValue<string>());
        Assert.Equal(outcome.MasterDataBackup, records[2]["masterDataBackup"]!.GetValue<string>());
        Assert.False(records[1].ContainsKey("backup"));
    }

    [Fact]
    public async Task ADryRun_AsksNobody_AndBacksNothingUp()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        WriteOutcome<JsonObject> outcome = await Run(dialog, dryRun: true);

        Assert.Equal(ExecutionState.NotStarted, outcome.Execution);
        Assert.Empty(dialog.Questions);
        Assert.Empty(_backups);
        Assert.False(Directory.Exists(_locations.Backups));
    }

    [Fact]
    public async Task MasterDataWritesDenied_IsRefusedBeforeAnyoneIsAsked()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        BridgeError error = await Fails(dialog, new HostConfig([_locations.DefaultScratch], OutsideScratchWrites.Approve) { MasterDataWrites = MasterDataWrites.Deny });

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted, PolicyEngine.MasterDataDenied), (error.Reason, error.Execution, error.Message));
        Assert.Empty(dialog.Questions);
        Assert.Empty(_backups);
    }

    [Fact]
    public async Task ADatabaseOnSqlServer_IsRefusedBeforeAnyoneIsAsked()
    {
        _server = true;
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        BridgeError error = await Fails(dialog);

        Assert.Equal((ErrorReason.PolicyDenied, PolicyEngine.MasterDataServerDenied), (error.Reason, error.Message));
        Assert.Empty(dialog.Questions);
    }

    [Theory]
    [InlineData(ErrorReason.BackupFailed, ErrorReason.BackupFailed)]
    [InlineData(ErrorReason.Timeout, ErrorReason.BackupFailed)]
    [InlineData(ErrorReason.EplanOperationFailed, ErrorReason.BackupFailed)]
    [InlineData(ErrorReason.PlanStale, ErrorReason.PlanStale)]
    public async Task ABackupThatFailed_RunsNothing_AndItsFolderIsMarkedFailed(string bridgeReason, string expected)
    {
        _bridge.Handlers[OperationNames.MasterDataBackup] = _ => throw new BridgeCallException(new BridgeError { Reason = bridgeReason, Message = "m" });

        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted));

        Assert.Equal((expected, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_bridge.Writes);
        if (expected == ErrorReason.BackupFailed)
        {
            Assert.EndsWith("Z-failed", Assert.Single(Directory.EnumerateDirectories(MasterDataBackups)), StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(Directory.EnumerateDirectories(MasterDataBackups));
        }

        JsonObject result = AuditRecords()[^1];
        Assert.Equal(("execution_result", ExecutionState.NotStarted, expected), (result["kind"]!.GetValue<string>(), result["execution"]!.GetValue<string>(), result["reason"]!.GetValue<string>()));
        Assert.Empty(SuspensionStore.For(_locations).All());
    }

    [Fact]
    public async Task AnArchiveOutsideTheFolder_IsNoBackup()
    {
        _bridge.Handlers[OperationNames.MasterDataBackup] = _ => new MasterDataBackupData { Database = Database, Archive = Path.Combine(_locations.Root, "parts.zw6"), Bytes = 6 };

        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted));

        Assert.Equal(ErrorReason.BackupFailed, error.Reason);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task AnUnknownOutcome_SuspendsTheDatabase_AndNamesItsBackup_ThenTheNextWriteAsksFirst()
    {
        _bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Message = "m", Execution = ExecutionState.Unknown };

        BridgeError error = await Fails(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted));

        Assert.Equal(Path.Combine(_backups[0].Folder, "parts.zw6"), error.Metadata!["backup"]);
        Assert.False(error.Metadata.ContainsKey("projectBackup"));
        IReadOnlyList<Suspension> suspended = SuspensionStore.For(_locations).All();
        Assert.Contains(suspended, s => s.Database == Database && s.Tool == Tool);
        Assert.DoesNotContain(suspended, s => s.Project is not null);

        // The next write to the database first asks the engineer to confirm they checked it, which they decline.
        _bridge.WriteFailure = null;
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied);
        BridgeError next = await Fails(dialog);

        Assert.Equal(ErrorReason.WritesSuspended, next.Reason);
        Assert.Equal(Database, dialog.Questions[0].Release!.Database);
    }

    private Task<WriteOutcome<JsonObject>> Run(IApprovalChannel dialog, bool dryRun = false, HostConfig? config = null)
    {
        var audit = new AuditLog(_locations.Audit, TimeProvider.System);
        config ??= HostConfig.Defaults(_locations);
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
        var request = new WriteRequest<object, object>(Tool, ToolTier.Destructive, Plan, Execute, new { file = "relays.xml" }, plan => new { plan.Fingerprint }, dryRun, TimeSpan.FromSeconds(180));
        return pipeline.RunAsync<object, object, JsonObject>(request, TestContext.Current.CancellationToken);
    }

    private async Task<BridgeError> Fails(IApprovalChannel dialog, HostConfig? config = null)
    {
        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run(dialog, config: config))).Error;
        Assert.True(ExecutionInvariants.Hold(error), $"{error.Reason}/{error.Execution}/{error.Retryable}");
        return error;
    }

    private List<JsonObject> AuditRecords() =>
        [.. Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).Order(StringComparer.Ordinal).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];
}

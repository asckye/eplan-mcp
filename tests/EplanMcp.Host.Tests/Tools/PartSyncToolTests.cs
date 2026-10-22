using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_part_sync through the real write pipeline against a fake Bridge (docs/tools/eplan_part_sync.md, sections 2 to
/// 7): the engineer is asked about the parts database and the project every time, both are backed up first, the result
/// is read back, a project with nothing to synchronize asks nobody, and an unknown outcome names both backups and
/// suspends the database and the project.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PartSyncToolTests : IDisposable
{
    private const string Database = @"$(MD_PARTS)\EPLAN_parts.alk";

    private readonly WriteToolHarness _harness;
    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private BackupTarget? _sent;
    private bool _nothingToDo;

    public PartSyncToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.PartSyncPlan] = _ => new WritePlan
        {
            Project = "Line1",
            ProjectPath = _harness.ProjectPath,
            Changes = _nothingToDo
                ? []
                :
                [
                    new PlannedChange { Action = "create", Kind = "parts", Name = "Line1", Property = "parts", NewValue = "1" },
                    new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "stored parts", OldValue = "293", NewValue = "48" },
                ],
            Fingerprint = "f1",
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = Database, Path = @"C:\Parts\EPLAN_parts.alk" },
            Details = new Dictionary<string, string>
            {
                ["added"] = "NEW.PART",
                ["addedCount"] = "1",
                ["updated"] = string.Empty,
                ["updatedCount"] = "0",
                ["skippedCount"] = "292",
                ["stored"] = "PXC.3209510",
                ["storedCount"] = "48",
                ["partsBefore"] = "465",
                ["projectPartsBefore"] = "293",
                ["mode"] = "append_new",
            },
        };
        _harness.Bridge.Handlers[OperationNames.MasterDataBackup] = args =>
        {
            var backup = (MasterDataBackupArgs)args!;
            string archive = Path.Combine(backup.Folder, backup.ArchiveName + ".zw6");
            File.WriteAllBytes(archive, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]);
            return new MasterDataBackupData { Database = backup.Database, Archive = archive, Bytes = 6 };
        };
        _harness.Bridge.Handlers[OperationNames.PartSyncExecute] = args =>
        {
            _sent = ((PartSyncArgs)args!).Backup;
            File.WriteAllText(_sent!.Path, "copy"); // the Bridge's copy of the project, as CopyProject leaves it
            return new PartSyncData
            {
                Project = "Line1",
                Database = Database,
                Added = ["NEW.PART"],
                AddedCount = 1,
                StoredInProject = ["PXC.3209510"],
                StoredInProjectCount = 48,
                PartsBefore = 465,
                PartsAfter = 466,
                PartsExpected = 466,
                ProjectPartsBefore = 293,
                ProjectPartsAfter = 341,
            };
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ASynchronization_IsApprovedWithBoth_BacksBothUpFirst_AndReadsBack()
    {
        CallToolResult result = await Sync();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "Line1", Database, "AI: eplan_part_sync"), (data.GetProperty("execution").GetString(), data.GetProperty("project").GetString(), data.GetProperty("database").GetString(), data.GetProperty("undo").GetString()));
        Assert.Equal((466, 341, 48), (data.GetProperty("partsAfter").GetInt32(), data.GetProperty("projectPartsAfter").GetInt32(), data.GetProperty("storedInProjectCount").GetInt32()));
        Assert.EndsWith("parts.zw6", data.GetProperty("backup").GetString(), StringComparison.Ordinal);
        Assert.Equal(_sent!.Path, data.GetProperty("projectBackup").GetString());
        Assert.Contains("Ctrl+Z in EPLAN undoes only the project's side", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal($"Added 1 part of the project to the parts database ({Database}); the project now also stores 48 parts it referenced. Both were backed up first (see 'backup' and 'projectBackup').", WriteToolHarness.Text(result, 0));

        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal((_harness.ProjectPath, Path.Combine(_harness.Locations.Backups, "Line1"), Path.Combine(_harness.Locations.Backups, "masterdata")), (question.Plan.ProjectPath, question.BackupFolder, question.MasterDataBackupFolder));
        Assert.Equal(["add 1 part from Line1 to the parts database", "store 48 parts the project references in project Line1 (now 293)"], question.Changes);
        var executed = (PartSyncArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.PartSyncExecute).Args!;
        Assert.Equal(("Line1", "append_new", "f1"), (executed.Project, executed.Mode, executed.Fingerprint));
    }

    [Fact]
    public async Task ADryRun_ListsThePartsFromThePlan_AndAsksNobody()
    {
        CallToolResult result = await Sync(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("backup").ValueKind, data.GetProperty("undo").ValueKind));
        Assert.Equal(["NEW.PART"], data.GetProperty("added").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["PXC.3209510"], data.GetProperty("storedInProject").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal((465, 293), (data.GetProperty("partsBefore").GetInt32(), data.GetProperty("projectPartsBefore").GetInt32()));
        Assert.Equal($"Dry run: would add 1 part of the project to the parts database ({Database}); the project would also store 48 parts it referenced. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task AProjectWithNothingToSynchronize_AsksNobody()
    {
        _nothingToDo = true;

        CallToolResult result = await Sync();

        Assert.StartsWith("Nothing to synchronize: with append_new", WriteToolHarness.Text(result, 0), StringComparison.Ordinal);
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task AnUnknownOutcome_NamesBothBackups_AndSuspendsTheDatabaseAndTheProject()
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Message = "m", Execution = ExecutionState.Unknown, Metadata = [] };

        JsonElement error = WriteToolHarness.Error(await Sync());

        JsonElement metadata = error.GetProperty("metadata");
        Assert.EndsWith("parts.zw6", metadata.GetProperty("backup").GetString(), StringComparison.Ordinal);
        Assert.EndsWith(@"Z\Line1.elk", metadata.GetProperty("projectBackup").GetString(), StringComparison.Ordinal);
        IReadOnlyList<Suspension> suspended = SuspensionStore.For(_harness.Locations).All();
        Assert.Contains(suspended, s => s.Database == Database);
        Assert.Contains(suspended, s => s.Project is not null);
    }

    [Fact]
    public async Task WithoutAProject_NothingIsAsked()
    {
        JsonElement error = WriteToolHarness.Error(await PartMasterDataTools.PartSyncAsync(_harness.Pipeline, " ", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Sync(bool dryRun = false) =>
        PartMasterDataTools.PartSyncAsync(_harness.Pipeline, "Line1", "append_new", dryRun, cancellationToken: TestContext.Current.CancellationToken);
}

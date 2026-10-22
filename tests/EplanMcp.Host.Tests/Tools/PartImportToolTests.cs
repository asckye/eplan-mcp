using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_part_import through the real write pipeline against a fake Bridge, with a parts file in the harness's import
/// folder (docs/tools/eplan_part_import.md, sections 2 to 7): the engineer is asked about the parts database every time,
/// it is backed up first, the result is read back, a file with nothing to import asks nobody, and an unknown outcome
/// names the backup and suspends the database.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PartImportToolTests : IDisposable
{
    private const string File = "supplier/relays.xml";
    private const string Database = @"$(MD_PARTS)\EPLAN_parts.alk";

    private readonly WriteToolHarness _harness;
    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly ImportFiles _files;
    private bool _nothingToDo;

    public PartImportToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _files = new ImportFiles(_harness.Locations);
        string path = Path.Combine(_harness.Locations.Imports, "supplier", "relays.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "<partsmanagement />");
        _harness.Bridge.Handlers[OperationNames.PartImportPlan] = _ => new WritePlan
        {
            Changes = _nothingToDo ? [] : [new PlannedChange { Action = "create", Kind = "parts", Name = File, Property = "parts", NewValue = "2" }],
            Fingerprint = "f1",
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = Database, Path = @"C:\Parts\EPLAN_parts.alk" },
            Details = new Dictionary<string, string>
            {
                ["file"] = "relays.xml (1 KB, sha256 0123456789ab…)",
                ["added"] = _nothingToDo ? string.Empty : "EPLANMCP.LARGE-TEST\nPXC.1234567",
                ["addedCount"] = _nothingToDo ? "0" : "2",
                ["updated"] = string.Empty,
                ["updatedCount"] = "0",
                ["skipped"] = "SIE.3RH2122-1HB40",
                ["skippedCount"] = "1",
                ["partsBefore"] = "464",
                ["mode"] = "append_new",
                ["matchBy"] = "part_number",
            },
        };
        _harness.Bridge.Handlers[OperationNames.MasterDataBackup] = args =>
        {
            var backup = (MasterDataBackupArgs)args!;
            string archive = Path.Combine(backup.Folder, backup.ArchiveName + ".zw6");
            System.IO.File.WriteAllBytes(archive, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]);
            return new MasterDataBackupData { Database = backup.Database, Archive = archive, Bytes = 6 };
        };
        _harness.Bridge.Handlers[OperationNames.PartImportExecute] = _ => new PartImportData
        {
            Database = Database,
            Added = ["EPLANMCP.LARGE-TEST", "PXC.1234567"],
            AddedCount = 2,
            Skipped = ["SIE.3RH2122-1HB40"],
            SkippedCount = 1,
            PartsBefore = 464,
            PartsAfter = 467,
            PartsExpected = 467,
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task AnImport_IsApprovedWithTheDatabaseNamed_BackedUpFirst_AndReadBack()
    {
        CallToolResult result = await Import();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", Database, File), (data.GetProperty("execution").GetString(), data.GetProperty("database").GetString(), data.GetProperty("file").GetString()));
        Assert.Equal(["EPLANMCP.LARGE-TEST", "PXC.1234567"], data.GetProperty("added").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal((464, 467, 1), (data.GetProperty("partsBefore").GetInt32(), data.GetProperty("partsAfter").GetInt32(), data.GetProperty("skippedCount").GetInt32()));
        string backup = data.GetProperty("backup").GetString()!;
        Assert.StartsWith(Path.Combine(_harness.Locations.Backups, "masterdata") + Path.DirectorySeparatorChar, backup, StringComparison.Ordinal);
        Assert.Contains("cannot be undone", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal($"Imported 2 new parts into the parts database ({Database}); left 1 part of the file unchanged. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));

        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal((ToolTier.Destructive, string.Empty), (question.Tier, question.Plan.ProjectPath));
        Assert.Equal(["add 2 parts from supplier/relays.xml to the parts database"], question.Changes);
        var executed = (PartImportArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.PartImportExecute).Args!;
        Assert.Equal((File, "append_new", "part_number", "f1"), (executed.File, executed.Mode, executed.MatchBy, executed.Fingerprint));
        Assert.Equal(64, executed.Sha256!.Length);
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task ADryRun_ListsThePartsFromThePlan_AsksNobody_AndBacksNothingUp()
    {
        CallToolResult result = await Import(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("backup").ValueKind));
        Assert.Equal(["EPLANMCP.LARGE-TEST", "PXC.1234567"], data.GetProperty("added").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["SIE.3RH2122-1HB40"], data.GetProperty("skipped").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal($"Dry run: would import 2 new parts into the parts database ({Database}); left 1 part of the file unchanged. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task AFileWithNothingToImport_AsksNobody()
    {
        _nothingToDo = true;

        CallToolResult result = await Import();

        Assert.Equal("not_started", WriteToolHarness.Structured(result).GetProperty("execution").GetString());
        Assert.StartsWith("Nothing to import: with append_new, every part of the file (1) stays as it is", WriteToolHarness.Text(result, 0), StringComparison.Ordinal);
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task AReadBackThatDoesNotMatch_IsInTheHint()
    {
        _harness.Bridge.Handlers[OperationNames.PartImportExecute] = _ => new PartImportData { Database = Database, Added = ["EPLANMCP.LARGE-TEST", "PXC.1234567"], AddedCount = 2, PartsBefore = 464, PartsAfter = 465, PartsExpected = 467, Missing = ["PXC.1234567"] };

        JsonElement data = WriteToolHarness.Structured(await Import(matchBy: "unique_id"));

        string hint = data.GetProperty("hint").GetString()!;
        Assert.Contains("465 parts instead of 467; not found: PXC.1234567", hint, StringComparison.Ordinal);
        Assert.Contains("with match_by unique_id EPLAN may decide otherwise", hint, StringComparison.Ordinal);
        Assert.Equal(["PXC.1234567"], data.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task AnUnknownOutcome_NamesTheBackup_AndSuspendsTheDatabase()
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Message = "m", Execution = ExecutionState.Unknown };

        JsonElement error = WriteToolHarness.Error(await Import());

        Assert.Equal(("OUTCOME_UNKNOWN", "unknown"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.EndsWith("parts.zw6", error.GetProperty("metadata").GetProperty("backup").GetString(), StringComparison.Ordinal);
        Assert.Contains(SuspensionStore.For(_harness.Locations).All(), s => s.Database == Database);
    }

    [Theory]
    [InlineData("supplier/relays.csv", "INVALID_ARGUMENT")]
    [InlineData("../relays.xml", "INVALID_ARGUMENT")]
    [InlineData("supplier/other.xml", "FILE_NOT_FOUND")]
    public async Task AFileTheImportsFolderDoesNotHold_IsRefused_BeforeTheBridgeIsAsked(string file, string reason)
    {
        JsonElement error = WriteToolHarness.Error(await Import(file: file));

        Assert.Equal(reason, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Import(string file = File, string matchBy = "part_number", bool dryRun = false) =>
        PartMasterDataTools.PartImportAsync(_harness.Pipeline, _files, file, "append_new", matchBy, dryRun, cancellationToken: TestContext.Current.CancellationToken);
}

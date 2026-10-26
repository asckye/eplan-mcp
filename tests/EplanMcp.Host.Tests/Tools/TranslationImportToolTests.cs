using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>The Host and write pipeline with real Bridge.Core operations and a fake translation database.</summary>
[Trait("Size", "Medium")]
public sealed class TranslationImportToolTests : IDisposable
{
    private const string Database = @"$(md_translate)\Translate.db";
    private const string FileName = "supplier/glossary.txt";
    private readonly WriteToolHarness _harness;
    private readonly FakeApprovalChannel _dialog;
    private readonly Writer _writer = new();
    private readonly Backup _backup = new();
    private ApprovalDecision? _approval = ApprovalDecision.Granted;

    public TranslationImportToolTests()
    {
        _dialog = new FakeApprovalChannel("eplan_dialog", null) { Answers = _ => _approval };
        _harness = new WriteToolHarness(_dialog);
        _writer.Path = Path.Combine(_harness.Locations.DefaultScratch, "Translate.db");
        string file = Path.Combine(_harness.Locations.Imports, "supplier", "glossary.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "en_US\tzh_CN\r\nNew\t新\r\nFill\t填\r\nKeep\t改\r\n", Encoding.Unicode);
        _harness.Bridge.Handlers[OperationNames.TranslationImportPlan] = args => Invoke(new TranslationImportPlanOperation(_writer), args!);
        _harness.Bridge.Handlers[OperationNames.MasterDataBackup] = args =>
        {
            Assert.NotEmpty(_dialog.Questions);
            Assert.Empty(_harness.Bridge.Writes);
            return Invoke(new MasterDataBackupOperation(_backup), args!);
        };
        _harness.Bridge.Handlers[OperationNames.TranslationImportExecute] = args =>
        {
            Assert.NotEmpty(_backup.Archives);
            Assert.True(File.Exists(_backup.Archives[^1]));
            return Invoke(new TranslationImportExecuteOperation(_writer), args!);
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Import_ApprovesAndBacksUpBeforeExecuting_AndLinksTheVerifiedResultToAudit()
    {
        CallToolResult result = await Import();
        JsonElement data = WriteToolHarness.Structured(result);

        Assert.Equal((Database, "committed", 3, "en_US", 1, 1, 1),
            (data.GetProperty("database").GetString(), data.GetProperty("execution").GetString(), data.GetProperty("entries").GetInt32(),
                data.GetProperty("referenceLanguage").GetString(), data.GetProperty("added").GetInt32(), data.GetProperty("filled").GetInt32(), data.GetProperty("kept").GetInt32()));
        string backup = data.GetProperty("backup").GetString()!;
        Assert.Equal(Assert.Single(_backup.Archives), backup);
        Assert.EndsWith("translations.zw7", backup, StringComparison.Ordinal);
        Assert.Equal($"Imported 3 entries (by en_US) into the translation database ({Database}): 1 added, 1 filled, 1 kept. Backup: {backup}", WriteToolHarness.Text(result, 0));
        Assert.Equal([OperationNames.TranslationImportPlan, OperationNames.MasterDataBackup, OperationNames.TranslationImportExecute], _harness.Bridge.Calls.Select(c => c.Operation));
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal((ToolTier.Destructive, string.Empty, MasterDataKinds.Translations), (question.Tier, question.Plan.ProjectPath, question.Plan.MasterData!.Kind));
        Assert.StartsWith(_harness.Locations.DefaultScratch, question.Plan.MasterData.Path, StringComparison.Ordinal);
        var executed = (TranslationImportArgs)_harness.Bridge.Calls[^1].Args!;
        Assert.Equal((FileName, "en_US", question.Plan.Fingerprint, Database), (executed.File, executed.ReferenceLanguage, executed.Fingerprint, executed.Database));
        Assert.Equal(executed.Sha256, executed.Fingerprint);
        Assert.Equal(TranslationImportTools.Limit, Assert.Single(_harness.Bridge.Writes).Limit);
        string auditId = data.GetProperty("auditId").GetString()!;
        Assert.Equal(_harness.Bridge.Writes[0].RequestId, auditId);
        List<JsonObject> records = Records();
        Assert.All(records, r => Assert.Equal(auditId, r["requestId"]!.GetValue<string>()));
        JsonObject started = Assert.Single(records, r => r["kind"]!.GetValue<string>() == "execution_started");
        Assert.Equal((FileName, executed.Sha256, "en_US"),
            (started["arguments"]!["file"]!.GetValue<string>(), started["arguments"]!["sha256"]!.GetValue<string>(), started["arguments"]!["referenceLanguage"]!.GetValue<string>()));
        JsonObject ended = records[^1];
        Assert.Equal(backup, ended["masterDataBackup"]!.GetValue<string>());
        Assert.Equal(Database, ended["masterData"]!["database"]!.GetValue<string>());
        Assert.Equal((3, 1, 1, 1), (ended["entries"]!.GetValue<int>(), ended["added"]!.GetValue<int>(), ended["filled"]!.GetValue<int>(), ended["kept"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Reimport_StillApprovesAndBacksUpEvenWhenAddedAndFilledAreZero()
    {
        await Import();
        _harness.Bridge.Writes.Clear();

        JsonElement again = WriteToolHarness.Structured(await Import());

        Assert.Equal(("committed", 0, 0, 1), (again.GetProperty("execution").GetString(), again.GetProperty("added").GetInt32(), again.GetProperty("filled").GetInt32(), again.GetProperty("kept").GetInt32()));
        Assert.Equal(2, _dialog.Questions.Count);
        Assert.Equal(2, _backup.Archives.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, _writer.Imports);
    }

    [Fact]
    public async Task DryRun_ReturnsTheValidatedPreviewWithoutApprovalBackupAuditOrInventedCounts()
    {
        JsonElement data = WriteToolHarness.Structured(await Import(dryRun: true));

        Assert.Equal(("not_started", true, 3), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("entries").GetInt32()));
        Assert.Equal("zh_CN", Assert.Single(data.GetProperty("languages").EnumerateArray()).GetString());
        Assert.Equal("新", data.GetProperty("preview")[0].GetProperty("zh_CN").GetString());
        foreach (string field in new[] { "added", "filled", "kept", "backup", "auditId" })
        {
            Assert.Equal(JsonValueKind.Null, data.GetProperty(field).ValueKind);
        }

        Assert.Empty(_dialog.Questions);
        Assert.Empty(_backup.Archives);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Audit));
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("expired")]
    [InlineData(null)]
    public async Task NoEngineerApproval_IsNeverAutoAllowedInScratch(string? decision)
    {
        _approval = decision switch
        {
            "denied" => ApprovalDecision.Denied,
            "expired" => ApprovalDecision.Expired,
            _ => null,
        };

        JsonElement error = WriteToolHarness.Error(await Import());

        Assert.Equal(decision == "expired" ? ErrorReason.ApprovalExpired : ErrorReason.ApprovalDenied, error.GetProperty("reason").GetString());
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        Assert.Single(_dialog.Questions);
        Assert.Empty(_backup.Archives);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Equal(0, _writer.Imports);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("empty")]
    [InlineData("wrong extension")]
    public async Task BackupFailure_PreventsImportAndDoesNotSuspend(string problem)
    {
        _backup.Problem = problem;

        JsonElement error = WriteToolHarness.Error(await Import());

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Single(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Equal(0, _writer.Imports);
        Assert.Empty(SuspensionStore.For(_harness.Locations).All());
    }

    [Theory]
    [InlineData("false")]
    [InlineData("missing")]
    public async Task FalseReturnOrMissingReadBack_IsUnknownWithBackupAndSuspension(string failure)
    {
        _writer.Failure = failure;

        JsonElement error = WriteToolHarness.Error(await Import());

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(Assert.Single(_backup.Archives), error.GetProperty("metadata").GetProperty("backup").GetString());
        Assert.Contains("Do not call again", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        IReadOnlyList<Suspension> suspensions = SuspensionStore.For(_harness.Locations).All();
        Assert.Contains(suspensions, s => s.Database == Database);
        Assert.Contains(suspensions, s => s.Database is null && s.Project is null && s.ProcessId == Environment.ProcessId);
        Assert.Equal(1, _writer.Imports);
        _approval = ApprovalDecision.Denied;
        Assert.Equal(ErrorReason.WritesSuspended, WriteToolHarness.Error(await Import()).GetProperty("reason").GetString());
        Assert.Equal(1, _writer.Imports);
    }

    [Fact]
    public async Task RecoveredResponse_HasTheAuditAndBackupWithoutInventingCounts()
    {
        _harness.Bridge.WriteRecovered = true;

        CallToolResult result = await Import();
        JsonElement data = WriteToolHarness.Structured(result);

        Assert.True(data.GetProperty("recovered").GetBoolean());
        foreach (string field in new[] { "added", "filled", "kept" })
        {
            Assert.Equal(JsonValueKind.Null, data.GetProperty(field).ValueKind);
            Assert.False(Records()[^1].ContainsKey(field));
        }

        Assert.Equal(Assert.Single(_backup.Archives), data.GetProperty("backup").GetString());
        Assert.Equal(Assert.Single(_harness.Bridge.Writes).RequestId, data.GetProperty("auditId").GetString());
        Assert.Contains("counts are unavailable", WriteToolHarness.Text(result, 0), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../glossary.txt", ErrorReason.InvalidArgument)]
    [InlineData("glossary.csv", ErrorReason.InvalidArgument)]
    [InlineData("missing.txt", ErrorReason.FileNotFound)]
    public async Task InvalidFiles_AreRejectedBeforeCallingTheBridge(string file, string reason)
    {
        Assert.Equal(reason, WriteToolHarness.Error(await Import(file: file)).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Fact]
    public async Task ReferenceLanguage_IsMappedAndValidatedByTheBridge()
    {
        JsonElement data = WriteToolHarness.Structured(await Import(reference: "zh_CN", dryRun: true));
        Assert.Equal("zh_CN", data.GetProperty("referenceLanguage").GetString());
        Assert.Equal("en_US", data.GetProperty("languages")[0].GetString());

        JsonElement error = WriteToolHarness.Error(await Import(reference: "en_US;zh_CN"));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        JsonElement violations = JsonDocument.Parse(error.GetProperty("metadata").GetProperty("violations").GetString()!).RootElement;
        Assert.Equal("/reference_language", Assert.Single(violations.EnumerateArray()).GetProperty("path").GetString());
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    private Task<CallToolResult> Import(string file = FileName, string reference = "en_US", bool dryRun = false) =>
        TranslationImportTools.TranslationImportAsync(_harness.Pipeline, new ImportFiles(_harness.Locations), file, reference, dryRun, cancellationToken: TestContext.Current.CancellationToken);

    private List<JsonObject> Records() =>
        [.. Directory.EnumerateFiles(_harness.Locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(line => JsonNode.Parse(line)!.AsObject())];

    private static object Invoke(IOperation operation, object args)
    {
        try
        {
            return operation.Execute(args)!;
        }
        catch (BridgeErrorException ex)
        {
            throw new BridgeCallException(ex.Error);
        }
    }

    private sealed class Writer : ITranslationDatabaseWriter
    {
        private const string Before = "en_US\tzh_CN\tCOMMENT\nFill\t\t\nKeep\t旧\t\n";
        private const string After = "en_US\tzh_CN\tCOMMENT\nFill\t填\t\nKeep\t旧\t\nNew\t新\t\n";
        public string Path { get; set; } = string.Empty;
        public string Failure { get; set; } = string.Empty;
        public int Imports { get; private set; }

        public ConfiguredDatabase Configured() => new() { Database = Database, Path = Path };

        public bool Export(string file)
        {
            File.WriteAllText(file, Imports == 0 || Failure == "missing" ? Before : After, Encoding.Unicode);
            return true;
        }

        public bool Import(string file, string referenceLanguage)
        {
            Assert.True(File.Exists(file));
            Assert.Equal("en_US", referenceLanguage);
            Imports++;
            return Failure != "false";
        }
    }

    private sealed class Backup : IMasterDataBackup
    {
        public List<string> Archives { get; } = [];
        public string Problem { get; set; } = string.Empty;

        public string TranslationDatabase() => Database;

        public string PartsDatabase() => throw new InvalidOperationException("Must not read the parts database.");

        public void BackupParts(string database, string folder, string archiveName) => throw new InvalidOperationException("Must not back up parts.");

        public void BackupTranslations(string database, string folder, string archiveName)
        {
            Assert.Equal(Database, database);
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
            if (Problem == "throw")
            {
                throw new IOException("backup failed");
            }

            string archive = Path.Combine(folder, archiveName + (Problem == "wrong extension" ? ".zw6" : ".zw7"));
            File.WriteAllBytes(archive, Problem == "empty" ? [] : [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]);
            Archives.Add(archive);
        }
    }
}

using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>TI-1 through a fake Bridge and the real backup validator; no registered translation-import tool.</summary>
[Trait("Size", "Medium")]
public sealed class TranslationImportSupportTests : IDisposable
{
    private const string Database = @"$(md_translate)\Translate.db";
    private const string Tool = "eplan_translation_import";
    private const string Plan = "test.translation.plan";
    private const string Execute = "test.translation.execute";
    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly TranslationBackup _backup = new();
    private readonly WriteToolHarness _harness;

    public TranslationImportSupportTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[Plan] = _ => new WritePlan
        {
            MasterData = new MasterDataTarget
            {
                Kind = MasterDataKinds.Translations,
                Database = Database,
                Path = Path.Combine(_harness.Locations.Root, "Translations", "Translate.db"),
            },
            Fingerprint = "sha256",
            Changes = [new PlannedChange { Action = "update", Kind = "translation", Name = "glossary.txt" }],
            Details = new Dictionary<string, string>
            {
                ["file"] = "glossary.txt",
                ["bytes"] = "120",
                ["sha256"] = "sha256",
                ["entries"] = "4",
                ["referenceLanguage"] = "en_US",
                ["languages"] = "zh_CN",
                ["preview"] = "[{\"en_US\":\"Motor\",\"zh_CN\":\"电机\"}]",
            },
        };
        _harness.Bridge.Handlers[OperationNames.MasterDataBackup] = args =>
        {
            Assert.NotEmpty(_dialog.Questions);
            Assert.Empty(_harness.Bridge.Writes);
            try
            {
                return new MasterDataBackupOperation(_backup).Execute(args!)!;
            }
            catch (BridgeErrorException ex)
            {
                throw new BridgeCallException(ex.Error);
            }
        };
        _harness.Bridge.Handlers[Execute] = _ => new JsonObject
        {
            ["referenceLanguage"] = "en_US",
            ["entries"] = 4,
            ["added"] = 1,
            ["filled"] = 2,
            ["kept"] = 3,
            ["unrelated"] = "not audit data",
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task TranslationBackup_IsApprovedFirst_ValidatedAsZw7_AndLinkedToResultAudit()
    {
        WriteOutcome<JsonObject> outcome = await Run();

        Assert.Equal(ExecutionState.Committed, outcome.Execution);
        Assert.Equal(MasterDataKinds.Translations, Assert.Single(_dialog.Questions).Plan.MasterData!.Kind);
        Assert.Equal([(Database, "translations")], _backup.Calls);
        Assert.EndsWith("translations.zw7", outcome.MasterDataBackup, StringComparison.Ordinal);
        Assert.Equal([Plan, OperationNames.MasterDataBackup, Execute], _harness.Bridge.Calls.Select(c => c.Operation));
        Assert.Equal(Assert.Single(_harness.Bridge.Writes).RequestId, outcome.AuditId);
        List<JsonObject> records = Records();
        Assert.All(records, r => Assert.Equal(outcome.AuditId, r["requestId"]!.GetValue<string>()));
        JsonObject result = records[^1];
        Assert.Equal(outcome.MasterDataBackup, result["masterDataBackup"]!.GetValue<string>());
        Assert.Equal(ExecutionState.Committed, result["execution"]!.GetValue<string>());
        Assert.Equal(Database, result["masterData"]!["database"]!.GetValue<string>());
        foreach (string field in new[] { "referenceLanguage", "entries", "added", "filled", "kept" })
        {
            Assert.True(JsonNode.DeepEquals(outcome.Data![field], result[field]), field);
        }

        Assert.False(result.ContainsKey("unrelated"));
    }

    [Fact]
    public async Task RepeatedCalls_EachRequireApprovalAndTheirOwnBackup()
    {
        await Run();
        _harness.Bridge.Writes.Clear();
        await Run();

        Assert.Equal(2, _dialog.Questions.Count);
        Assert.Equal(2, _backup.Calls.Count);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(_harness.Locations.Backups, "masterdata")).Length);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("empty")]
    [InlineData("not 7z")]
    [InlineData("parts archive")]
    [InlineData("two files")]
    [InlineData("exception")]
    public async Task AnInvalidTranslationBackup_PreventsExecution(string problem)
    {
        _backup.Problem = problem;

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run())).Error;

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("translation database", error.Message, StringComparison.Ordinal);
        Assert.Single(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Equal(ExecutionState.NotStarted, Records()[^1]["execution"]!.GetValue<string>());
        Assert.Empty(SuspensionStore.For(_harness.Locations).All());
    }

    [Theory]
    [InlineData("SQL|Translation|2|user|secret", ErrorReason.PolicyDenied)]
    [InlineData("another.db", ErrorReason.PlanStale)]
    [InlineData("", ErrorReason.PlanStale)]
    public async Task AServerOrChangedTranslationDatabase_IsRefusedBeforeBackup(string configured, string reason)
    {
        _backup.Configured = configured;

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run())).Error;

        Assert.Equal((reason, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.Empty(_backup.Calls);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ADryRun_HasNoApprovalBackupOrAuditId()
    {
        WriteOutcome<JsonObject> outcome = await Run(dryRun: true);

        Assert.Equal(ExecutionState.NotStarted, outcome.Execution);
        Assert.Null(outcome.AuditId);
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_backup.Calls);
        Assert.False(Directory.Exists(_harness.Locations.Audit));
    }

    [Fact]
    public async Task ARejectedApproval_DoesNotBackUpOrExecute()
    {
        var denied = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(denied);
        harness.Bridge.Handlers[Plan] = _harness.Bridge.Handlers[Plan];

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run(harness: harness))).Error;

        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Single(denied.Questions);
        Assert.Equal(Plan, Assert.Single(harness.Bridge.Calls).Operation);
        Assert.Empty(harness.Bridge.Writes);
    }

    [Fact]
    public async Task AnUnknownOutcome_NamesTheZw7AndSuspendsTheDatabaseAndInstance()
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Execution = ExecutionState.Unknown, Message = "unknown" };

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => Run())).Error;

        Assert.Equal(ExecutionState.Unknown, error.Execution);
        Assert.EndsWith("translations.zw7", error.Metadata!["backup"], StringComparison.Ordinal);
        IReadOnlyList<Suspension> suspensions = SuspensionStore.For(_harness.Locations).All();
        Assert.Contains(suspensions, s => s.Database == Database);
        Assert.Contains(suspensions, s => s.Database is null && s.Project is null && s.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public async Task ARecoveredCommit_HasAnAuditIdWithoutInventedCounts()
    {
        _harness.Bridge.WriteRecovered = true;

        WriteOutcome<JsonObject> outcome = await Run();

        Assert.NotNull(outcome.AuditId);
        Assert.True(outcome.Recovered);
        Assert.False(Records()[^1].ContainsKey("added"));
    }

    private Task<WriteOutcome<JsonObject>> Run(bool dryRun = false, WriteToolHarness? harness = null) =>
        (harness ?? _harness).Pipeline.RunAsync<object, object, JsonObject>(
            new WriteRequest<object, object>(Tool, ToolTier.Destructive, Plan, Execute, new { file = "glossary.txt", referenceLanguage = "en_US" },
                plan => new { plan.Fingerprint }, dryRun, TimeSpan.FromSeconds(180)), TestContext.Current.CancellationToken);

    private List<JsonObject> Records() =>
        [.. Directory.EnumerateFiles(_harness.Locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];

    private sealed class TranslationBackup : IMasterDataBackup
    {
        public string Configured { get; set; } = Database;
        public string Problem { get; set; } = string.Empty;
        public List<(string Database, string Archive)> Calls { get; } = [];

        public string PartsDatabase() => throw new InvalidOperationException("A translation backup must not read the parts database.");

        public void BackupParts(string database, string folder, string archiveName) => throw new InvalidOperationException("A translation backup must not back up parts.");

        public string TranslationDatabase() => Configured;

        public void BackupTranslations(string database, string folder, string archiveName)
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
            Calls.Add((database, archiveName));
            if (Problem == "exception")
            {
                throw new InvalidOperationException("backup returned false");
            }

            if (Problem == "nothing")
            {
                return;
            }

            byte[] content = Problem switch
            {
                "empty" => [],
                "not 7z" => "PK zip"u8.ToArray(),
                _ => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C],
            };
            File.WriteAllBytes(Path.Combine(folder, archiveName + (Problem == "parts archive" ? ".zw6" : ".zw7")), content);
            if (Problem == "two files")
            {
                File.WriteAllText(Path.Combine(folder, "other.txt"), "extra file");
            }
        }
    }
}

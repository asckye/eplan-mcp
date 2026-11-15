using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>The Host and write pipeline with real Bridge.Core operations and a fake translation database.</summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class TranslationAddToolTests : IDisposable
{
    private const string Database = @"$(md_translate)\Translate.db";
    private readonly WriteToolHarness _harness;
    private readonly FakeApprovalChannel _dialog;
    private readonly Writer _writer = new();
    private readonly Backup _backup = new();
    private ApprovalDecision? _approval = ApprovalDecision.Granted;

    public TranslationAddToolTests()
    {
        _dialog = new FakeApprovalChannel("eplan_dialog", null) { Answers = _ => _approval };
        _harness = new WriteToolHarness(_dialog);
        _writer.Path = Path.Combine(_harness.Locations.DefaultScratch, "Translate.db");
        _harness.Bridge.Handlers[OperationNames.TranslationKeywordSetPlan] = args => Invoke(new TranslationKeywordSetPlanOperation(_writer), args!);
        _harness.Bridge.Handlers[OperationNames.MasterDataBackup] = args =>
        {
            Assert.NotEmpty(_dialog.Questions);
            Assert.Empty(_harness.Bridge.Writes);
            return Invoke(new MasterDataBackupOperation(_backup), args!);
        };
        _harness.Bridge.Handlers[OperationNames.TranslationKeywordSetExecute] = args =>
        {
            Assert.NotEmpty(_backup.Archives);
            Assert.True(File.Exists(_backup.Archives[^1]));
            return Invoke(new TranslationKeywordSetExecuteOperation(_writer), args!);
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Write_ApprovesAndBacksUpBeforeExecuting_AndCountsWhatTheDatabaseTook()
    {
        CallToolResult result = await Set();
        JsonElement data = WriteToolHarness.Structured(result);

        Assert.Equal((Database, "committed", 3, "en_US", 1, 1, 1),
            (data.GetProperty("database").GetString(), data.GetProperty("execution").GetString(), data.GetProperty("keywords").GetInt32(),
                data.GetProperty("referenceLanguage").GetString(), data.GetProperty("added").GetInt32(), data.GetProperty("filled").GetInt32(), data.GetProperty("kept").GetInt32()));
        string backup = data.GetProperty("backup").GetString()!;
        Assert.Equal(Assert.Single(_backup.Archives), backup);
        Assert.EndsWith("translations.zw7", backup, StringComparison.Ordinal);
        Assert.Equal($"Wrote 3 keywords (by en_US) in the translation database ({Database}): 1 added, 1 filled, 1 kept. Backup: {backup}", WriteToolHarness.Text(result, 0));
        Assert.Equal([OperationNames.TranslationKeywordSetPlan, OperationNames.MasterDataBackup, OperationNames.TranslationKeywordSetExecute], _harness.Bridge.Calls.Select(c => c.Operation));
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal((ToolTier.Destructive, string.Empty, MasterDataKinds.Translations), (question.Tier, question.Plan.ProjectPath, question.Plan.MasterData!.Kind));
        var executed = (TranslationKeywordSetArgs)_harness.Bridge.Calls[^1].Args!;
        Assert.Equal(("en_US", question.Plan.Fingerprint, Database), (executed.ReferenceLanguage, executed.Fingerprint, executed.Database));
        Assert.Equal(TranslationAddTools.Limit, Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.Equal(_harness.Bridge.Writes[0].RequestId, data.GetProperty("auditId").GetString());
        JsonObject ended = Records()[^1];
        Assert.Equal(Database, ended["masterData"]!["database"]!.GetValue<string>());
        Assert.Equal((1, 1, 1), (ended["added"]!.GetValue<int>(), ended["filled"]!.GetValue<int>(), ended["kept"]!.GetValue<int>()));
    }

    [Fact]
    public async Task DryRun_ShowsTheAddedChangesAndTheTranslationsItWouldLeaveAlone()
    {
        CallToolResult result = await Set(dryRun: true);
        JsonElement data = WriteToolHarness.Structured(result);

        Assert.Equal(("not_started", true, 3), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("keywords").GetInt32()));
        Assert.Equal(
        [
            ("create", "New", (string?)null),
            ("update", "New", "zh_CN"),
            ("update", "Fill", "zh_CN"),
        ],
        data.GetProperty("changes").EnumerateArray().Select(change => (
            change.GetProperty("action").GetString()!,
            change.GetProperty("name").GetString()!,
            change.TryGetProperty("property", out JsonElement property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null)));
        Assert.Equal("Keep [zh_CN] 旧", Assert.Single(data.GetProperty("keptTranslations").EnumerateArray()).GetString());
        Assert.Contains("1 existing translations left alone", WriteToolHarness.Text(result, 0), StringComparison.Ordinal);
        foreach (string field in new[] { "added", "filled", "kept", "backup", "auditId" })
        {
            Assert.Equal(JsonValueKind.Null, data.GetProperty(field).ValueKind);
        }

        Assert.Empty(_dialog.Questions);
        Assert.Empty(_backup.Archives);
        Assert.Equal(0, _writer.Imports);
        Assert.False(Directory.Exists(_harness.Locations.Audit));
    }

    [Fact]
    public async Task McpDefault_PreviewsAndCleansEveryTextItReturns()
    {
        Func<object?, object> plan = _harness.Bridge.Handlers[OperationNames.TranslationKeywordSetPlan];
        _harness.Bridge.Handlers[OperationNames.TranslationKeywordSetPlan] = input =>
        {
            WritePlan result = Assert.IsType<WritePlan>(plan(input));
            foreach (PlannedChange change in result.Changes)
            {
                change.Name += "\u0007‮";
            }

            result.Details!["keptTranslations"] += "\u0007‮";
            return result;
        };
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            Tool definition = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), item => item.Name == TranslationAddTools.Tool).ProtocolTool;
            Assert.True(definition.InputSchema.GetProperty("properties").GetProperty("dry_run").GetProperty("default").GetBoolean());

            JsonElement data = WriteToolHarness.Structured(await client.CallToolAsync(
                TranslationAddTools.Tool,
                new Dictionary<string, object?> { ["keywords"] = Keywords() },
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.True(data.GetProperty("dryRun").GetBoolean());
            Assert.Matches("^[0-9a-f]{64}$", data.GetProperty("fingerprint").GetString()!);
            Assert.Contains("dry_run: false", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
            Assert.Equal(0, _writer.Imports);
            Assert.Empty(_dialog.Questions);
            foreach (string text in data.GetProperty("changes").EnumerateArray().Select(change => change.GetProperty("name").GetString()!)
                .Concat(data.GetProperty("keptTranslations").EnumerateArray().Select(kept => kept.GetString()!)))
            {
                Assert.DoesNotContain('\u0007', text);
                Assert.DoesNotContain('‮', text);
            }
        });
    }

    [Fact]
    public async Task NoEngineerApproval_IsNeverAutoAllowedInScratch()
    {
        _approval = ApprovalDecision.Denied;

        JsonElement error = WriteToolHarness.Error(await Set());

        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Single(_dialog.Questions);
        Assert.Empty(_backup.Archives);
        Assert.Equal(0, _writer.Imports);
    }

    [Fact]
    public async Task AFailedImport_IsUnknownWithTheBackupAndSuspendsFurtherWrites()
    {
        _writer.Failure = "false";

        JsonElement error = WriteToolHarness.Error(await Set());

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(Assert.Single(_backup.Archives), error.GetProperty("metadata").GetProperty("backup").GetString());
        Assert.Contains("Do not call again", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains(SuspensionStore.For(_harness.Locations).All(), suspension => suspension.Database == Database);
        Assert.Equal(1, _writer.Imports);

        // A keyword the first call did not write, so this one has something to do and reaches the suspension, which
        // asks the engineer to confirm they checked the database; refusing that leaves the writes suspended.
        _approval = ApprovalDecision.Denied;
        JsonElement blocked = WriteToolHarness.Error(await Set(JsonSerializer.Deserialize<JsonElement[]>(
            """[{"source": "Later", "translations": {"zh_CN": "后来"}}]""")!));

        Assert.Equal(ErrorReason.WritesSuspended, blocked.GetProperty("reason").GetString());
        Assert.Equal(1, _writer.Imports);
    }

    [Fact]
    public async Task AnEntryThatIsNotAnObject_IsRejectedBeforeCallingTheBridge()
    {
        JsonElement error = WriteToolHarness.Error(await Set(JsonSerializer.Deserialize<JsonElement[]>("""["Motor", 7]""")!));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("'keywords[0]' must be an object", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Fact]
    public async Task TheBridgesArgumentAndLanguageChecks_ReachTheModelWithoutApprovalOrBackup()
    {
        JsonElement empty = WriteToolHarness.Error(await Set([]));
        Assert.Contains("1 to 200 entries", empty.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);

        JsonElement unknown = WriteToolHarness.Error(await Set(JsonSerializer.Deserialize<JsonElement[]>(
            """[{"source": "New", "translations": {"fr_FR": "Nouveau"}}]""")!));

        Assert.Equal(ErrorReason.InvalidArgument, unknown.GetProperty("reason").GetString());
        Assert.Contains("no language fr_FR", unknown.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_backup.Archives);
        Assert.Equal(0, _writer.Imports);
    }

    private static JsonElement[] Keywords() => JsonSerializer.Deserialize<JsonElement[]>("""
        [
          {"source": "New", "translations": {"zh_CN": "新"}},
          {"source": "Fill", "translations": {"zh_CN": "填"}},
          {"source": "Keep", "translations": {"zh_CN": "改"}}
        ]
        """)!;

    private Task<CallToolResult> Set(JsonElement[]? keywords = null, bool dryRun = false) =>
        TranslationAddTools.TranslationAddAsync(
            _harness.Pipeline, keywords ?? Keywords(), "en_US", dryRun, cancellationToken: TestContext.Current.CancellationToken);

    private Task<CallToolResult> Set(bool dryRun) => Set(null, dryRun);

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
            File.WriteAllText(file, Imports == 0 ? Before : After, Encoding.Unicode);
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

        public string TranslationDatabase() => Database;

        public string PartsDatabase() => throw new InvalidOperationException("Must not read the parts database.");

        public void BackupParts(string database, string folder, string archiveName) => throw new InvalidOperationException("Must not back up parts.");

        public void BackupTranslations(string database, string folder, string archiveName)
        {
            Assert.Equal(Database, database);
            string archive = System.IO.Path.Combine(folder, archiveName + ".zw7");
            File.WriteAllBytes(archive, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]);
            Archives.Add(archive);
        }
    }
}

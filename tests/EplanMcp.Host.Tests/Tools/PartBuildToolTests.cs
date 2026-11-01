using System.Text.Json;
using System.Xml.Linq;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PartBuildToolTests : IDisposable
{
    private readonly WriteToolHarness _harness;
    private readonly Store _store = new();
    private readonly FakeApprovalChannel _approval;
    private readonly ImportFiles _files;
    private const string Csv = "part_number,manufacturer,description.en_US,technical_data,function_category,connection_points\nFICTION.BUILD1,FICTION,Relay,DC 24V,200,A1|A2\nFICTION.BUILD2,FICTION,Contact,,,\nFICTION.BUILD3,FICTION,Terminal,,,";

    public PartBuildToolTests()
    {
        _approval = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted) { Wait = () => DuringApproval?.Invoke(), Answers = _ => DenyApproval ? ApprovalDecision.Denied : ApprovalDecision.Granted };
        _harness = new WriteToolHarness(_approval);
        _files = new ImportFiles(_harness.Locations);
        Directory.CreateDirectory(_files.Root);
        File.WriteAllText(Path.Combine(_files.Root, "parts.csv"), Csv);
        _harness.Bridge.Handlers[OperationNames.PartSearch] = args =>
        {
            if (FailReadback && _store.Imports > 0)
            { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "Readback failed." }); }
            if (CancelReadback && _store.Imports > 0)
            { throw new OperationCanceledException("Readback cancelled after commit."); }
            var search = (PartSearchArgs)args!;
            List<PartSearchItem> items = [.. _store.Read(null).Parts.Where(p => search.BuildNumbers!.Contains(p.PartNumber, StringComparer.Ordinal))];
            if (CorruptReadback && _store.Imports > 0)
            { items[0].BuildValues["manufacturer"] = "DIFFERENT"; }
            return new PartSearchData { Database = _store.Configured().Database, Items = items, Total = items.Count };
        };
        _harness.Bridge.Handlers[OperationNames.PartImportPlan] = args => Invoke(() => new PartImportPlanOperation(_store, _store).Execute(args!));
        _harness.Bridge.Handlers[OperationNames.PartImportExecute] = args => Invoke(() => new PartImportExecuteOperation(_store, _store).Execute(args!));
        _harness.Bridge.Handlers[OperationNames.MasterDataBackup] = args =>
        {
            var backup = (MasterDataBackupArgs)args!;
            string archive = Path.Combine(backup.Folder, backup.ArchiveName + ".zw6");
            if (!FailBackup)
            { File.WriteAllBytes(archive, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]); }
            return new MasterDataBackupData { Database = Store.Database, Archive = archive, Bytes = FailBackup ? 0 : 6 };
        };
    }

    private Action? DuringApproval { get; set; }
    private bool FailBackup { get; set; }
    private bool FailReadback { get; set; }
    private bool CancelReadback { get; set; }
    private bool DenyApproval { get; set; }
    private bool CorruptReadback { get; set; }
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DryRun_ReportsEveryRow_WithoutAnyWriteApprovalOrTemporaryFile()
    {
        JsonElement result = WriteToolHarness.Structured(await Build(dry: true));
        Assert.Equal("not_started", result.GetProperty("execution").GetString());
        Assert.Equal(["create", "create", "create"], result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("action").GetString()));
        Assert.Equal([2, 3, 4], result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("rowNumber").GetInt32()));
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Equal(0, _store.Imports);
        Assert.Single(Directory.GetFiles(_files.Root));
        Assert.Empty(Directory.GetDirectories(_files.Root));
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task Execute_ApprovesAndBacksUp_ImportsExportCompatibleXml_AndReadsFieldsBack()
    {
        JsonElement result = WriteToolHarness.Structured(await Build());
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Equal(3, result.GetProperty("import").GetProperty("addedCount").GetInt32());
        Assert.Equal(3, result.GetProperty("readBack").GetArrayLength());
        Assert.True(File.Exists(result.GetProperty("import").GetProperty("backup").GetString()));
        ApprovalQuestion question = Assert.Single(_approval.Questions);
        Assert.Equal((PartBuildTools.Tool, ToolTier.Destructive, Store.Database), (question.Tool, question.Tier, question.Plan.MasterData!.Database));
        Assert.Contains(question.Changes, c => c.Contains("function_category", StringComparison.Ordinal));
        Assert.Contains(question.Changes, c => c.Contains("sha256", StringComparison.Ordinal));
        Assert.Equal(["FICTION.BUILD1", "FICTION.BUILD2", "FICTION.BUILD3"], _store.LastXmlNumbers);
        Assert.Equal(1, _store.Imports);
        Assert.Empty(Directory.GetDirectories(_files.Root));
        Assert.True(_harness.Bridge.Calls.FindIndex(c => c.Operation == OperationNames.MasterDataBackup) < _harness.Bridge.Calls.FindIndex(c => c.Operation == OperationNames.PartImportExecute));
    }

    [Fact]
    public async Task NoChanges_DoesNotApproveBackUpOrCreateTemporaryFiles()
    {
        File.WriteAllText(Path.Combine(_files.Root, "parts.csv"), "part_number,manufacturer\nA,ACME");
        _store.Values["A"] = new() { ["manufacturer"] = "ACME" };
        JsonElement result = WriteToolHarness.Structured(await Build(mode: "update_existing"));
        Assert.Equal("skip", result.GetProperty("rows")[0].GetProperty("action").GetString());
        Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(Directory.GetDirectories(_files.Root));
    }

    [Fact]
    public async Task DeniedMasterDataPolicy_PreventsApprovalBackupAndImport()
    {
        HostConfig config = HostConfig.Defaults(_harness.Locations) with { MasterDataWrites = MasterDataWrites.Deny };
        var audit = new AuditLog(_harness.Locations.Audit, TimeProvider.System);
        var suspensions = SuspensionStore.For(_harness.Locations);
        var pipeline = new WritePipeline(_harness.Bridge, config, suspensions, audit,
            new DanglingWrites(_harness.Bridge, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService([_approval], config, TimeProvider.System, NullLogger<ApprovalService>.Instance),
            new BackupFolders(_harness.Locations, TimeProvider.System), TimeProvider.System, NullLogger<WritePipeline>.Instance);

        JsonElement error = WriteToolHarness.Error(await PartBuildTools.BuildAsync(_harness.Bridge, pipeline, _files, "parts.csv", dry_run: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(Directory.GetDirectories(_files.Root));
        Assert.Equal(0, _store.Imports);
    }

    [Fact]
    public async Task SqlServerPartsDatabase_IsDeniedBeforeApprovalAndImport()
    {
        _store.ConfiguredName = "server|database";
        JsonElement error = WriteToolHarness.Error(await Build());
        Assert.Equal(ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(Directory.GetDirectories(_files.Root));
        Assert.Equal(0, _store.Imports);
    }

    [Theory]
    [InlineData("append_new", "skip", 2)]
    [InlineData("update_existing", "update", 1)]
    [InlineData("update_and_append", "update", 3)]
    public async Task Modes_FilterAcceptedRowsAndReportFieldDifferences(string mode, string action, int expected)
    {
        _store.Values["FICTION.BUILD1"] = new() { ["manufacturer"] = "OLD", ["description.de_DE"] = "Relais" };
        JsonElement preview = WriteToolHarness.Structured(await Build(mode: mode, dry: true));
        JsonElement row = preview.GetProperty("rows")[0];
        Assert.Equal(action, row.GetProperty("action").GetString());
        Assert.Equal(["OLD", "FICTION"], row.GetProperty("diff").GetProperty("manufacturer").EnumerateArray().Select(e => e.GetString()));
        JsonElement executed = WriteToolHarness.Structured(await Build(mode: mode));
        Assert.Equal(expected == 0 ? "not_started" : "committed", executed.GetProperty("execution").GetString());
        Assert.Equal(expected, _store.LastXmlNumbers.Count);
        if (expected > 0)
        { Assert.Equal("Relais", _store.Values["FICTION.BUILD1"]["description.de_DE"]); }
    }

    [Fact]
    public async Task ErrorRows_BlockTheEntireBatch_AndReportUnknownColumns()
    {
        File.WriteAllText(Path.Combine(_files.Root, "parts.csv"), "part_number,width,Unknown\nA,-1,x\nB,1,y");
        JsonElement result = WriteToolHarness.Structured(await Build());
        Assert.Equal("error", result.GetProperty("rows")[0].GetProperty("action").GetString());
        Assert.Equal("create", result.GetProperty("rows")[1].GetProperty("action").GetString());
        Assert.Equal("Unknown", result.GetProperty("unknownColumns")[0].GetString());
        Assert.Empty(_approval.Questions);
        Assert.Equal(0, _store.Imports);
    }

    [Fact]
    public async Task ReferencePaths_StayBelowImports_AndLockedDuringApproval()
    {
        File.WriteAllText(Path.Combine(_files.Root, "parts.csv"), "part_number,macro_file,picture_file\nA,relay.ema,relay.png");
        File.WriteAllText(Path.Combine(_files.Root, "relay.ema"), "fiction");
        File.WriteAllText(Path.Combine(_files.Root, "relay.png"), "fiction");
        DuringApproval = () => Assert.Throws<IOException>(() => File.WriteAllText(Path.Combine(_files.Root, "relay.ema"), "changed"));
        JsonElement result = WriteToolHarness.Structured(await Build());
        Assert.Equal("relay.ema", result.GetProperty("readBack")[0].GetProperty("macro_file").GetString());
        Assert.DoesNotContain(_files.Root, result.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(_files.Root, "relay.ema"), _store.Values["A"]["macro_file"]);
    }

    [Theory]
    [InlineData("../secret.ema")]
    [InlineData("C:/outside.ema")]
    [InlineData("$(MD_MACROS)/relay.ema")]
    [InlineData("missing.ema")]
    public async Task InvalidReferences_AreRowErrors_WithoutApproval(string reference)
    {
        File.WriteAllText(Path.Combine(_files.Root, "parts.csv"), "part_number,macro_file\nA," + reference);
        JsonElement result = WriteToolHarness.Structured(await Build());
        Assert.Equal("error", result.GetProperty("rows")[0].GetProperty("action").GetString());
        Assert.Empty(_approval.Questions);
        Assert.Equal(0, _store.Imports);
    }

    [Fact]
    public async Task BackupFailure_PreventsImport_AndCleansTemporaryXml()
    {
        FailBackup = true;
        Assert.Equal(ErrorReason.BackupFailed, WriteToolHarness.Error(await Build()).GetProperty("reason").GetString());
        Assert.Equal(0, _store.Imports);
        Assert.Empty(Directory.GetDirectories(_files.Root));
    }

    [Fact]
    public async Task ApprovalDenial_DoesNotBackUpOrImport_AndReleasesTheSourceFile()
    {
        DenyApproval = true;
        DuringApproval = () => Assert.Throws<IOException>(() => File.WriteAllText(Path.Combine(_files.Root, "parts.csv"), "changed"));
        JsonElement error = WriteToolHarness.Error(await Build());
        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(0, _store.Imports);
        Assert.DoesNotContain(_harness.Bridge.Calls, c => c.Operation == OperationNames.MasterDataBackup);
        Assert.Empty(Directory.GetDirectories(_files.Root));
        File.WriteAllText(Path.Combine(_files.Root, "parts.csv"), "released");
    }

    [Fact]
    public async Task FieldChangesDuringApproval_AreStale_EvenWhenPartNumbersDidNotChange()
    {
        _store.Values["FICTION.BUILD1"] = new() { ["manufacturer"] = "OLD" };
        DuringApproval = () => _store.Values["FICTION.BUILD1"]["manufacturer"] = "CHANGED";
        JsonElement error = WriteToolHarness.Error(await Build(mode: "update_existing"));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(0, _store.Imports);
        Assert.Empty(Directory.GetDirectories(_files.Root));
    }

    [Fact]
    public async Task VariantChangesDuringApproval_AreStale()
    {
        _store.Values["FICTION.BUILD1"] = new() { ["manufacturer"] = "OLD" };
        DuringApproval = () => _store.Variant = "2";
        JsonElement error = WriteToolHarness.Error(await Build(mode: "update_existing"));
        Assert.Equal(ErrorReason.PlanStale, error.GetProperty("reason").GetString());
        Assert.Equal(0, _store.Imports);
    }

    [Fact]
    public async Task NewPartAppearingDuringApproval_IsStale()
    {
        DuringApproval = () => _store.Values["FICTION.BUILD1"] = new() { ["manufacturer"] = "OTHER" };
        JsonElement error = WriteToolHarness.Error(await Build());
        Assert.Equal(ErrorReason.PlanStale, error.GetProperty("reason").GetString());
        Assert.Equal(0, _store.Imports);
    }

    [Fact]
    public async Task ImportFailure_IsUnknown_NamesBackupAndSuspendsDatabase()
    {
        _store.FailImport = true;
        JsonElement error = WriteToolHarness.Error(await Build());
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.True(File.Exists(error.GetProperty("metadata").GetProperty("backup").GetString()));
        Assert.Contains(SuspensionStore.For(_harness.Locations).All(), s => s.Database == Store.Database);
        Assert.Empty(Directory.GetDirectories(_files.Root));
    }

    [Fact]
    public async Task ReadBackFailure_PreservesCommittedOutcomeWithWarning()
    {
        FailReadback = true;
        JsonElement result = WriteToolHarness.Structured(await Build());
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Contains("read-back failed", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadBackCancellation_PreservesCommittedOutcomeAndBackup()
    {
        CancelReadback = true;
        JsonElement result = WriteToolHarness.Structured(await Build());
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.True(File.Exists(result.GetProperty("import").GetProperty("backup").GetString()));
        Assert.Contains("read-back failed", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FieldReadBackMismatch_IsCommittedButExplicitlyWarned()
    {
        CorruptReadback = true;
        JsonElement result = WriteToolHarness.Structured(await Build());
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Contains("field read-back differs", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("DIFFERENT", result.GetProperty("readBack")[0].GetProperty("manufacturer").GetString());
    }

    [Theory]
    [InlineData("../parts.csv", ErrorReason.InvalidArgument)]
    [InlineData("parts.xml", ErrorReason.InvalidArgument)]
    [InlineData("missing.csv", ErrorReason.FileNotFound)]
    public async Task InvalidSourcePaths_NeverReachTheBridge(string file, string reason)
    {
        Assert.Equal(reason, WriteToolHarness.Error(await Build(file: file)).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Build(bool dry = false, string mode = "append_new", string file = "parts.csv") =>
        PartBuildTools.BuildAsync(_harness.Bridge, _harness.Pipeline, _files, file, mode: mode, dry_run: dry, cancellationToken: TestContext.Current.CancellationToken);

    private static object Invoke(Func<object?> call)
    {
        try
        { return call()!; }
        catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
    }

    private sealed class Store : IPartsDatabaseWriter, IPartsDatabase
    {
        internal const string Database = @"$(MD_PARTS)\FictionCopy.alk";
        internal Dictionary<string, Dictionary<string, string>> Values { get; } = new(StringComparer.Ordinal);
        internal int Imports { get; private set; }
        internal List<string> LastXmlNumbers { get; private set; } = [];
        internal bool FailImport { get; set; }
        internal string ConfiguredName { get; set; } = Database;
        internal string Variant { get; set; } = "1";
        public ConfiguredDatabase Configured() => new() { Database = ConfiguredName, Path = @"C:\FictionCopy\Parts.alk" };
        public PartsDatabaseState Read() => new() { PartNumbers = new HashSet<string>(Values.Keys, StringComparer.Ordinal), PartCount = Values.Count };
        public PartsDatabaseContent Read(string? language, IReadOnlyList<string>? buildNumbers = null) => new() { Database = ConfiguredName, Parts = [.. Values.Select(p => new PartSearchItem { PartNumber = p.Key, Variant = Variant, BuildValues = new(p.Value, StringComparer.Ordinal) })] };
        public void Synchronize(string projectPath, string mode) => throw new NotSupportedException();
        public void Import(string file, string mode, string matchBy)
        {
            if (FailImport)
            { throw new IOException("Simulated import failure."); }
            LastXmlNumbers = [.. PartsXmlFile.Read(file, "generated.xml").Select(p => p.PartNumber)];
            foreach (XElement part in XDocument.Load(file).Root!.Elements("part"))
            {
                string number = part.Attribute("P_ARTICLE_PARTNR")!.Value;
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["part_type"] = (string?)part.Attribute("P_ARTICLE_PARTTYPE") ?? "1";
                foreach ((string key, string xml) in PartBuildResult.Columns.Where(p => p.Key != "part_number" && p.Value.Length > 0))
                { row[key] = (string?)(key == "technical_data" ? part.Element("variant")! : part).Attribute("P_ARTICLE_" + xml) ?? ""; }
                foreach (string entry in ((string?)part.Attribute("P_ARTICLE_DESCR1") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                { row["description." + entry[..5]] = entry[6..]; }
                XElement? template = part.Element("variant")!.Element("functiontemplate");
                row["function_category"] = (string?)template?.Attribute("functiondefcategory") ?? "";
                row["connection_points"] = ((string?)template?.Attribute("connectionDesignation") ?? "").Replace('\n', '|');
                Values[number] = row;
            }
            Imports++;
        }
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>Real UTF-16 files and a fake translation database; no EPLAN is loaded or started.</summary>
[Trait("Size", "Medium")]
public sealed class TranslationImportOperationTests : IDisposable
{
    private const string Database = @"$(md_translate)\Translate.db";
    private const string Input = "zh_CN\ten_US\tde_DE\r\n新\tNew\tNeu\r\n填\tFill\tFüllen\r\n新译文\tKeep\tNeu\r\n相同\tSame\tGleich\r\n新\tNew\tNeu\r\n";
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-translation-test-").FullName;
    private readonly Writer _writer = new();
    private readonly string _file;

    public TranslationImportOperationTests()
    {
        _file = Path.Combine(_folder, "glossary.txt");
        File.WriteAllText(_file, Input, Encoding.Unicode);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Plan_DescribesTheFileAndDatabaseWithoutExportingOrImporting()
    {
        TranslationImportArgs args = Args();

        WritePlan plan = Plan(args);

        Assert.Equal(OperationNames.TranslationImportPlan, new TranslationImportPlanOperation(_writer).Name);
        IOperation operation = new TranslationImportPlanOperation(_writer);
        Assert.False(operation is IWriteOperation);
        Assert.Equal((string.Empty, string.Empty), (plan.Project, plan.ProjectPath));
        Assert.Equal((MasterDataKinds.Translations, Database, @"C:\Translations\Translate.db", false),
            (plan.MasterData!.Kind, plan.MasterData.Database, plan.MasterData.Path, plan.MasterData.Server));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "translations", "glossary.txt", "entries", "5"),
            (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Equal(args.Sha256, plan.Fingerprint);
        Assert.Equal(("glossary.txt", new FileInfo(_file).Length.ToString(CultureInfo.InvariantCulture), args.Sha256, "5", "en_US", "zh_CN, de_DE"),
            (plan.Details!["file"], plan.Details["bytes"], plan.Details["sha256"], plan.Details["entries"], plan.Details["referenceLanguage"], plan.Details["languages"]));
        Assert.Equal("新", JArray.Parse(plan.Details["preview"])[0]!["zh_CN"]);
        Assert.Empty(_writer.Files);
    }

    [Fact]
    public void Plan_PreviewsOnlyTenRowsWithFortyCharactersPerLanguage()
    {
        File.WriteAllText(_file, "en_US\tzh_CN\n" + string.Join("\n", Enumerable.Range(0, 12).Select(i =>
            i.ToString(CultureInfo.InvariantCulture) + new string('x', 60) + "\t" + new string('文', 50))), Encoding.Unicode);

        WritePlan plan = Plan(Args());
        JArray preview = JArray.Parse(plan.Details!["preview"]);

        Assert.Equal("12", plan.Details["entries"]);
        Assert.Equal(10, preview.Count);
        Assert.All(preview, row => Assert.All(((JObject)row).Properties(), cell => Assert.Equal(40, cell.Value.Value<string>()!.Length)));
        Assert.Single(plan.Changes);
    }

    [Fact]
    public void Fingerprint_IsTheFileHashAndIgnoresTimestampAndDatabaseContents()
    {
        string before = Plan(Args()).Fingerprint;
        File.SetLastWriteTimeUtc(_file, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _writer.Before = "en_US\tzh_CN\nUnrelated\t无关\n";

        Assert.Equal(before, Plan(Args()).Fingerprint);

        File.AppendAllText(_file, "阀\tValve\tVentil\r\n", Encoding.Unicode);

        Assert.NotEqual(before, Plan(Args()).Fingerprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_UsesTheBeforeExportsExactHeaderAndOneReferenceLanguage(bool bigEndian)
    {
        File.WriteAllText(_file, Input, new UnicodeEncoding(bigEndian, true, true));

        TranslationImportData data = Execute(PlannedArgs());

        Assert.True(new TranslationImportExecuteOperation(_writer) is IWriteOperation);
        Assert.Equal(OperationNames.TranslationImportExecute, new TranslationImportExecuteOperation(_writer).Name);
        Assert.Equal((Database, 5, "en_US", 1, 2, 2), (data.Database, data.Entries, data.ReferenceLanguage, data.Added, data.Filled, data.Kept));
        Assert.Equal(["export", "import:en_US", "export"], _writer.Calls);
        Assert.Equal("de_DE\ten_US\tzh_CN\tCOMMENT\r\nNeu\tNew\t新\t\r\nFüllen\tFill\t填\t\r\nNeu\tKeep\t新译文\t\r\nGleich\tSame\t相同\t\r\nNeu\tNew\t新\t\r\n", _writer.ImportText);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, _writer.ImportBytes!.Take(2));
        Assert.Equal(Input, File.ReadAllText(_file, Encoding.Unicode));
        AssertTemporaryFilesDeleted();
    }

    [Fact]
    public void Execute_RepeatedInputStillRunsAndCountsNoNewEntriesOrFills()
    {
        TranslationImportArgs args = PlannedArgs();
        Execute(args);
        _writer.Before = _writer.After;

        Assert.Single(Plan(Args()).Changes);
        TranslationImportData data = Execute(args);

        Assert.Equal((0, 0, 2), (data.Added, data.Filled, data.Kept));
        Assert.Equal(2, _writer.Calls.Count(call => call == "import:en_US"));
        AssertTemporaryFilesDeleted();
    }

    [Fact]
    public void Execute_UsesTheSelectedReferenceLanguage()
    {
        File.WriteAllText(_file, "en_US\tzh_CN\nMotor\t电机\n", Encoding.Unicode);
        _writer.Before = "zh_CN\ten_US\tCOMMENT\n\tMotor\t\n";
        _writer.After = "zh_CN\ten_US\tCOMMENT\n电机\tMotor\t\n";

        TranslationImportData data = Execute(PlannedArgs("zh_CN"));

        Assert.Equal(("zh_CN", 1), (data.ReferenceLanguage, data.Added));
        Assert.Contains("import:zh_CN", _writer.Calls);
        AssertTemporaryFilesDeleted();
    }

    [Theory]
    [InlineData("en_US;zh_CN", "/reference_language")]
    [InlineData("fr_FR", "/reference_language")]
    [InlineData("", "/reference_language")]
    public void Plan_RejectsInvalidReferenceLanguagesWithToolParameterPointers(string reference, string parameterPath)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(reference))).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(parameterPath, Assert.Single(JArray.Parse(error.Metadata!["violations"])).Value<string>("path"));
        Assert.Empty(_writer.Calls);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("empty reference")]
    [InlineData("bad header")]
    public void Plan_RejectsInvalidFilesBeforeReadingTheDatabase(string problem)
    {
        File.WriteAllText(_file, problem == "empty reference" ? "en_US\tzh_CN\n\t空\n" : "English\nMotor\n",
            problem == "utf8" ? Encoding.UTF8 : Encoding.Unicode);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains(JArray.Parse(error.Metadata!["violations"]), v => v.Value<string>("path") == "/file");
        Assert.Equal(0, _writer.ConfiguredReads);
    }

    [Theory]
    [InlineData("missing", ErrorReason.FileNotFound)]
    [InlineData("too large", ErrorReason.InvalidArgument)]
    [InlineData("extension", ErrorReason.InvalidArgument)]
    [InlineData("hash", ErrorReason.InvalidArgument)]
    public void Plan_ValidatesFileMetadataBeforeParsing(string problem, string reason)
    {
        TranslationImportArgs args = Args();
        switch (problem)
        {
            case "missing":
                File.Delete(_file);
                break;
            case "too large":
                using (FileStream stream = File.OpenWrite(_file))
                {
                    stream.SetLength(TranslationImportPlanOperation.MaxFileBytes + 1);
                }

                break;
            case "extension":
                args.FilePath = Path.Combine(_folder, "glossary.csv");
                break;
            case "hash":
                args.Sha256 = "ABC";
                break;
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal((reason, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, _writer.ConfiguredReads);
    }

    [Fact]
    public void Plan_LockedFileIsInvalidWithoutLeakingItsFullPath()
    {
        TranslationImportArgs args = Args();
        using var locked = new FileStream(_file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain(_folder, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _writer.ConfiguredReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileChangedSinceTheHostReadIt_IsStaleBeforePlanOrExecution(bool execute)
    {
        TranslationImportArgs args = PlannedArgs();
        File.AppendAllText(_file, "阀\tValve\tVentil\r\n", Encoding.Unicode);

        BridgeError error = Assert.Throws<BridgeErrorException>(() =>
        {
            if (execute)
            {
                Execute(args);
            }
            else
            {
                Plan(args);
            }
        }).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_writer.Calls);
    }

    [Theory]
    [InlineData("missing fingerprint", ErrorReason.InvalidArgument)]
    [InlineData("missing database", ErrorReason.InvalidArgument)]
    [InlineData("fingerprint", ErrorReason.PlanStale)]
    [InlineData("database", ErrorReason.PlanStale)]
    [InlineData("server", ErrorReason.PolicyDenied)]
    public void Execute_RejectsUnboundOrChangedPlansBeforeExporting(string problem, string reason)
    {
        TranslationImportArgs args = PlannedArgs();
        switch (problem)
        {
            case "missing fingerprint":
                args.Fingerprint = null;
                break;
            case "missing database":
                args.Database = null;
                break;
            case "fingerprint":
                args.Fingerprint = new string('0', 64);
                break;
            case "database":
                _writer.Database = "another.db";
                break;
            case "server":
                _writer.Database = "SQL|Translation|2|user|secret";
                break;
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((reason, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.Empty(_writer.Calls);
    }

    [Theory]
    [InlineData("", ErrorReason.EplanOperationFailed)]
    [InlineData("SQL|Translation|2|user|secret", ErrorReason.PolicyDenied)]
    public void Plan_RejectsMissingOrServerDatabases(string database, string reason)
    {
        _writer.Database = database;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;

        Assert.Equal((reason, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.Empty(_writer.Calls);
    }

    [Fact]
    public void Execute_UnsupportedColumnsAreInvalidBeforeImportAndTemporaryFilesAreDeleted()
    {
        _writer.Before = "en_US\tCOMMENT\n";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(PlannedArgs())).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(["zh_CN", "en_US", "de_DE"], JArray.Parse(error.Metadata!["columns"]).Values<string>());
        Assert.Equal("/file", Assert.Single(JArray.Parse(error.Metadata["violations"])).Value<string>("path"));
        Assert.Equal(["export"], _writer.Calls);
        AssertTemporaryFilesDeleted();
    }

    [Theory]
    [InlineData("before false")]
    [InlineData("before throw")]
    [InlineData("before missing")]
    public void Execute_FailedBeforeExportDoesNotStartImportAndCleansUp(string failure)
    {
        _writer.Failure = failure;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(PlannedArgs())).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(["export"], _writer.Calls);
        AssertTemporaryFilesDeleted();
    }

    [Theory]
    [InlineData("import false")]
    [InlineData("import throw")]
    [InlineData("after false")]
    [InlineData("after throw")]
    [InlineData("after missing")]
    [InlineData("after malformed")]
    [InlineData("missing entry")]
    public void Execute_ImportOrReadBackFailureIsUnknownAndCleansUp(string failure)
    {
        _writer.Failure = failure;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(PlannedArgs())).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Equal(TranslationImportExecuteOperation.UnknownMessage, error.Message);
        Assert.Contains("metadata.backup", error.Message, StringComparison.Ordinal);
        Assert.Contains("Do not call again", error.Message, StringComparison.Ordinal);
        Assert.Single(_writer.Calls, call => call == "import:en_US");
        AssertTemporaryFilesDeleted();
    }

    [Fact]
    public void Execute_CancelBeforeImportIsNotStartedAndCleansUp()
    {
        var context = new OperationContext("translation-cancel", write: true, progress: null);
        _writer.OnExport = context.Cancel;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(PlannedArgs()))).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(["export"], _writer.Calls);
        AssertTemporaryFilesDeleted();
    }

    [Fact]
    public void Execute_CancelAfterImportStillVerifiesTheCommittedResult()
    {
        var context = new OperationContext("translation-cancel-after", write: true, progress: null);
        _writer.OnImport = context.Cancel;

        var data = (TranslationImportData)context.Run(() => Execute(PlannedArgs()))!;

        Assert.Equal((1, 2, 2), (data.Added, data.Filled, data.Kept));
        Assert.Equal(["export", "import:en_US", "export"], _writer.Calls);
        AssertTemporaryFilesDeleted();
    }

    [Theory]
    [InlineData("import false", ExecutionState.Unknown)]
    [InlineData("missing entry", ExecutionState.Unknown)]
    [InlineData("before false", ExecutionState.NotStarted)]
    public void Router_RecordsTheActualExecutionStateWithoutClaimingRollback(string failure, string execution)
    {
        _writer.Failure = failure;
        var router = new OperationRouter([new TranslationImportPlanOperation(_writer), new TranslationImportExecuteOperation(_writer)],
            new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));
        TranslationImportArgs args = Args();
        JToken plan = router.Invoke(OperationNames.TranslationImportPlan, JObject.FromObject(args), "translation-plan");
        args.Fingerprint = plan.Value<string>("fingerprint");
        args.Database = plan["masterData"]!.Value<string>("database");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(
            OperationNames.TranslationImportExecute, JObject.FromObject(args), "translation-write")).Error;
        JToken recorded = router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = "translation-write" }, "translation-outcome");

        Assert.Equal(execution, error.Execution);
        Assert.Equal(execution, recorded.Value<string>("status"));
        AssertTemporaryFilesDeleted();
    }

    private TranslationImportArgs Args(string referenceLanguage = "en_US") => new()
    {
        File = "glossary.txt",
        FilePath = _file,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_file))),
        ReferenceLanguage = referenceLanguage,
    };

    private TranslationImportArgs PlannedArgs(string referenceLanguage = "en_US")
    {
        TranslationImportArgs args = Args(referenceLanguage);
        WritePlan plan = Plan(args);
        args.Fingerprint = plan.Fingerprint;
        args.Database = plan.MasterData!.Database;
        return args;
    }

    private WritePlan Plan(TranslationImportArgs args) => (WritePlan)new TranslationImportPlanOperation(_writer).Execute(args)!;

    private TranslationImportData Execute(TranslationImportArgs args) => (TranslationImportData)new TranslationImportExecuteOperation(_writer).Execute(args)!;

    private void AssertTemporaryFilesDeleted()
    {
        Assert.NotEmpty(_writer.Files);
        Assert.All(_writer.Files, file =>
        {
            Assert.False(File.Exists(file), file);
            Assert.False(Directory.Exists(Path.GetDirectoryName(file)), file);
        });
    }

    private sealed class Writer : ITranslationDatabaseWriter
    {
        public string Database { get; set; } = TranslationImportOperationTests.Database;
        public string Before { get; set; } = "de_DE\ten_US\tzh_CN\tCOMMENT\n\tFill\t\tcomment\nAlt\tKeep\t原译文\t\nGleich\tSame\t相同\t\n";
        public string After { get; set; } = "zh_CN\tde_DE\ten_US\tCOMMENT\n新\tNeu\tNew\t\n填\tFüllen\tFill\tcomment\n原译文\tAlt\tKeep\t\n相同\tGleich\tSame\t\n";
        public string Failure { get; set; } = string.Empty;
        public int ConfiguredReads { get; private set; }
        public List<string> Calls { get; } = [];
        public List<string> Files { get; } = [];
        public string? ImportText { get; private set; }
        public byte[]? ImportBytes { get; private set; }
        public Action? OnExport { get; set; }
        public Action? OnImport { get; set; }

        public ConfiguredDatabase Configured()
        {
            ConfiguredReads++;
            return new ConfiguredDatabase { Database = Database, Path = @"C:\Translations\Translate.db" };
        }

        public bool Export(string file)
        {
            bool after = Calls.Contains("import:en_US", StringComparer.Ordinal) || Calls.Contains("import:zh_CN", StringComparer.Ordinal);
            string phase = after ? "after" : "before";
            Calls.Add("export");
            Files.Add(file);
            OnExport?.Invoke();
            if (Failure == phase + " throw")
            {
                throw new InvalidOperationException("export failed");
            }

            if (Failure == phase + " missing")
            {
                return true;
            }

            string content = after && Failure != "missing entry" ? After : Before;
            File.WriteAllText(file, content, Failure == "after malformed" && after ? Encoding.UTF8 : Encoding.Unicode);
            return Failure != phase + " false";
        }

        public bool Import(string file, string referenceLanguage)
        {
            Calls.Add("import:" + referenceLanguage);
            Files.Add(file);
            ImportBytes = File.ReadAllBytes(file);
            ImportText = File.ReadAllText(file, Encoding.Unicode);
            OnImport?.Invoke();
            if (Failure == "import throw")
            {
                throw new InvalidOperationException("import failed after partially changing the database");
            }

            return Failure != "import false";
        }
    }
}

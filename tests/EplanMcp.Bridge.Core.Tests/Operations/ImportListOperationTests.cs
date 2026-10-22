using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>List import decisions with in-memory file observations, a fake backup and S34 business counts.</summary>
[Trait("Size", "Small")]
public sealed class ImportListOperationTests
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    private static readonly string _sha256 = new('a', 64);
    private readonly FakeCatalog _catalog = new(_project);
    private readonly FakePageReader _pages = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Importer _importer = new();
    private readonly List<string> _steps = [];
    private readonly ProgressSlot _progress = new();
    private readonly OperationContext _context;
    private string _observedHash = _sha256;
    private long _bytes = 14000000;
    private long _written = 100;
    private TimeSpan _elapsed;
    private Action? _duringBackup;

    public ImportListOperationTests()
    {
        _context = new("list-import", write: true, _progress);
        _transactions.Snapshot = () =>
        {
            Assert.Contains("backup", _steps);
            ListImportState before = _importer.State;
            return () => _importer.State = before;
        };
    }

    [Theory]
    [InlineData("devices", null, "xml", "XDLXmlExporter")]
    [InlineData("devices", "xml", "xml", "XDLXmlExporter")]
    [InlineData("devices", "csv", "csv", "XDLCsvImporterExporter")]
    [InlineData("parts", "xml", "xml", "XPalXmlExporter")]
    public void Plan_FixedConverters_CountsAndOneProjectUpdate(string kind, string? format, string extension, string converter)
    {
        ImportListArgs args = Args(kind, format, extension);
        WritePlan plan = Plan(args);

        Assert.Equal((_project.Name, _project.Path), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", kind, "Line1", "list", null), (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Contains("only be checked after running", change.OldValue, StringComparison.Ordinal);
        Assert.Equal((args.File, "14000000", _sha256, converter), (plan.Details!["file"], plan.Details["bytes"], plan.Details["sha256"], plan.Details["converter"]));
        Assert.Equal("{\"deviceListEntries\":0,\"projectParts\":293,\"articleReferences\":2900}", plan.Details["before"]);
        Assert.Empty(_importer.Imports);
        Assert.Empty(_transactions.Calls);
        Assert.DoesNotContain("backup", _steps);
    }

    [Theory]
    [InlineData("parts", "csv", "csv")]
    [InlineData("parts", "yaml", "yaml")]
    [InlineData("devices", "CSV", "csv")]
    [InlineData("devices", "", "xml")]
    [InlineData("other", "xml", "xml")]
    [InlineData("devices", "xml", "csv")]
    public void Plan_InvalidKindFormatOrExtension_RejectsBeforeReading(string kind, string format, string extension)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(kind, format, extension))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.NotEmpty(error.Metadata!["violations"]);
        Assert.Empty(_steps);
        Assert.Equal(0, _importer.Reads);
    }

    [Fact]
    public void Plan_ReportsAllViolationsTogether()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new ImportListArgs { Kind = "unknown", Format = "yaml" })).Error;

        string violations = error.Metadata!["violations"];
        foreach (string field in new[] { "project", "kind", "format", "file", "filePath", "sha256" })
        {
            Assert.Contains("'" + field + "'", violations, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(104857601)]
    public void Plan_EmptyOrOversizedFile_IsInvalid(long bytes)
    {
        _bytes = bytes;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(0, _importer.Reads);
    }

    [Fact]
    public void Plan_MaximumFileSize_IsAccepted()
    {
        _bytes = 100 * 1024 * 1024;
        Assert.Equal("104857600", Plan(Args()).Details!["bytes"]);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("time")]
    [InlineData("same-count-field")]
    [InlineData("kind")]
    public void Execute_FileOrProjectFingerprintChanged_IsStaleBeforeBackup(string change)
    {
        ImportListArgs args = Planned();
        switch (change)
        {
            case "hash":
                _observedHash = new string('b', 64);
                break;
            case "size":
                _bytes++;
                break;
            case "time":
                _written++;
                break;
            case "same-count-field":
                _importer.State = new(_importer.State.Counts, "changed-part-number");
                break;
            case "kind":
                args.Kind = "parts";
                break;
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain("backup", _steps);
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_importer.Imports);
    }

    [Fact]
    public void Execute_HashChangedAfterBackup_IsStaleWithoutImport()
    {
        ImportListArgs args = Planned();
        _duringBackup = () => _observedHash = new string('b', 64);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("backup", _steps);
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_importer.Imports);
    }

    [Fact]
    public void Execute_ProjectChangedDuringBackup_IsStaleWithoutImport()
    {
        ImportListArgs args = Planned();
        _duringBackup = () => _importer.State = new(_importer.State.Counts, "changed-reference-count-field");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_importer.Imports);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_BudgetUsedByBackup_NeverCallsAction()
    {
        ImportListArgs args = Planned();
        _duringBackup = () => _elapsed = TimeSpan.FromSeconds(301);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(_importer.Imports);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_ReportsBackupImportAndReadbackPhases()
    {
        ImportListArgs args = Planned();
        _duringBackup = () => Assert.Equal("Backing up project before list import", _progress.Take()!.Message);
        _importer.DuringImport = () => Assert.Equal("Importing project list", _progress.Take()!.Message);
        _importer.AfterReadback = () => Assert.Equal("Reading back project list counts and references", _progress.Take()!.Message);

        Assert.Equal(2, Execute(args).After.DeviceListEntries);
    }

    [Fact]
    public void Execute_BackupFailure_NeverImports()
    {
        ImportListArgs args = Planned();
        _duringBackup = () => throw new BridgeErrorException(ErrorReason.BackupFailed, ProjectBackups.FailedMessage);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_importer.Imports);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("throw")]
    [InlineData("readback")]
    [InlineData("budget")]
    public void Execute_ActionOrReadbackFailure_RestoresBeforeState(string failure)
    {
        ImportListArgs args = Planned();
        _importer.Failure = failure;
        _importer.DuringImport = () =>
        {
            if (failure == "budget")
            {
                _elapsed = TimeSpan.FromSeconds(301);
            }
        };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(2900, _importer.State.Counts.ArticleReferences);
        Assert.Equal(["Begin AI: eplan_import_list", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("backup")]
    [InlineData("action")]
    [InlineData("readback")]
    public void Execute_Cancellation_StopsAtSafePoint(string when)
    {
        ImportListArgs args = Planned();
        if (when == "before")
        {
            _context.Cancel();
        }

        _duringBackup = () => { if (when == "backup") { _context.Cancel(); } };
        _importer.DuringImport = () => { if (when == "action") { _context.Cancel(); } };
        _importer.AfterReadback = () => { if (when == "readback") { _context.Cancel(); } };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal(ErrorReason.Cancelled, error.Reason);
        Assert.Equal(when is "action" or "readback" ? ExecutionState.RolledBack : ExecutionState.NotStarted, error.Execution);
        Assert.Equal(2900, _importer.State.Counts.ArticleReferences);
        Assert.DoesNotContain("Commit", _transactions.Calls);
        if (when is "before" or "backup")
        {
            Assert.Empty(_importer.Imports);
        }
    }

    [Theory]
    [InlineData("devices", "xml", 2, 2900)]
    [InlineData("devices", "csv", 2, 2900)]
    [InlineData("parts", "xml", 0, 2902)]
    public void Execute_Success_ReturnsBusinessCountsAndFileWithoutPredictingRows(string kind, string format, int entries, int references)
    {
        ImportListArgs args = Planned(kind, format);
        ImportListData data = Execute(args);

        Assert.Equal(("Line1", kind, args.File, _bytes, _sha256), (data.Project, data.Kind, data.File, data.Bytes, data.Sha256));
        Assert.Equal((0, 293, 2900), (data.Before.DeviceListEntries, data.Before.ProjectParts, data.Before.ArticleReferences));
        Assert.Equal((entries, 293, references), (data.After.DeviceListEntries, data.After.ProjectParts, data.After.ArticleReferences));
        Assert.Equal((_project.Path, kind, args.FilePath, data.Converter), Assert.Single(_importer.Imports));
        Assert.Equal(["Begin AI: eplan_import_list", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Contains("undo after commit have not been verified", data.Warning, StringComparison.Ordinal);
        var json = BridgeJson.FromObject(data);
        Assert.Equal(references, (int)json["after"]!["articleReferences"]!);
        Assert.Null(json["importedRows"]);
    }

    [Fact]
    public void Execute_NoCountChange_StillCommitsWithReadbackLimits()
    {
        ImportListArgs args = Planned();
        _importer.NoChange = true;
        ImportListData data = Execute(args);
        Assert.Equal(0, data.After.DeviceListEntries);
        Assert.Contains("Commit", _transactions.Calls);
        Assert.Contains("row-level effects", data.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RollbackFailure_IsUnknown()
    {
        ImportListArgs args = Planned();
        _importer.Failure = "false";
        _transactions.FailAt = "Rollback";
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Fact]
    public void Execute_PostCommitFailure_IsSuccessWithWarning()
    {
        ImportListArgs args = Planned();
        _transactions.FailAt = "CloseUndo";
        ImportListData data = Execute(args);
        Assert.Equal(2, data.After.DeviceListEntries);
        Assert.Contains("did not close its undo step", data.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutFingerprint_IsInvalidAndNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args())).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_steps);
    }

    private static ImportListArgs Args(string kind = "devices", string? format = null, string extension = "xml") => new()
    {
        Project = "Line1",
        Kind = kind,
        Format = format,
        File = "incoming/list." + extension,
        FilePath = @"D:\Imports\incoming\list." + extension,
        Sha256 = _sha256,
        Backup = new BackupTarget { Path = @"D:\Backups\Line1.elk", MaxSeconds = 150 },
    };

    private ImportListArgs Planned(string kind = "devices", string format = "xml")
    {
        ImportListArgs args = Args(kind, format, format);
        args.Fingerprint = Plan(args).Fingerprint;
        _steps.Clear();
        return args;
    }

    private void Read(ImportSource source)
    {
        _steps.Add("file");
        source.Observe(_bytes, _written, _observedHash);
    }

    private WritePlan Plan(ImportListArgs args) => (WritePlan)new ImportListPlanOperation(_catalog, _importer, Read).Execute(args)!;

    private ImportListData Execute(ImportListArgs args) => (ImportListData)_context.Run(() =>
        new ImportListExecuteOperation(_catalog, _pages, _importer, new FakeProjectBackup(_pages), _transactions, Read,
            (project, target) =>
            {
                Assert.Same(_project, project);
                Assert.Same(args.Backup, target);
                _steps.Add("backup");
                _duringBackup?.Invoke();
            }, () => () => _elapsed).Execute(args))!;

    private sealed class Importer : IListImporter
    {
        public ListImportState State { get; set; } = new(new ListImportCounts { ProjectParts = 293, ArticleReferences = 2900 }, "before");
        public int Reads { get; private set; }
        public string? Failure { get; set; }
        public bool NoChange { get; set; }
        public Action? DuringImport { get; set; }
        public Action? AfterReadback { get; set; }
        public List<(string Project, string Kind, string File, string Converter)> Imports { get; } = [];

        public ListImportState Read(string projectPath)
        {
            Reads++;
            if (Imports.Count > 0)
            {
                if (Failure == "readback")
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "List readback failed.");
                }

                AfterReadback?.Invoke();
            }

            return State;
        }

        public bool Import(string projectPath, string kind, string filePath, string converter)
        {
            Imports.Add((projectPath, kind, filePath, converter));
            if (!NoChange)
            {
                State = new(new ListImportCounts
                {
                    DeviceListEntries = kind == "devices" ? 2 : 0,
                    ProjectParts = 293,
                    ArticleReferences = kind == "parts" ? 2902 : 2900,
                }, "after");
            }

            DuringImport?.Invoke();
            return Failure == "throw" ? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Import failed.") : Failure != "false";
        }
    }
}

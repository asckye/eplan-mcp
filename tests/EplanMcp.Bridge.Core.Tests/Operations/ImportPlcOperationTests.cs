using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>Primary import protection with memory-only file/backup seams and an importer that can leave partial changes.</summary>
[Trait("Size", "Small")]
public sealed class ImportPlcOperationTests
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    private static readonly string _sha256 = new('a', 64);
    private readonly FakeCatalog _catalog = new(_project);
    private readonly Importer _importer = new();
    private readonly FakeTransactions _transactions = new();
    private readonly List<string> _steps = [];
    private readonly FakePageReader _pages = new();
    private readonly FakeProjectBackup _backup = new();
    private long _bytes = 1024;
    private long _written = 123;
    private string _hash = _sha256;
    private Action? _duringBackup;

    public ImportPlcOperationTests()
    {
        _importer.Functions = [new(1, "=F+A2-KE1", "PLCBox", true), new(2, "=F+A2-X1:1", "Terminal", false)];
        _importer.Added = [new(3, "=G+A2-KE1", "PLCBox", true), new(4, "=G+A2-X1:1", "Terminal", false)];
        _transactions.Snapshot = () =>
        {
            Assert.Contains("Backup", _steps);
            _steps.Add("Begin");
            List<PlcImportFunction> before = [.. _importer.Functions];
            int references = _importer.ArticleReferences;
            return () => { _importer.Functions = before; _importer.ArticleReferences = references; };
        };
    }

    [Fact]
    public void Plan_IsOneProjectUpdate_WithCountsAndIncompletePreflight_AndNoImport()
    {
        WritePlan plan = Plan(Args());

        Assert.Equal(("Line1", _project.Path), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "project", "Line1", null), (change.Action, change.Kind, change.Name, change.NewValue));
        Assert.Contains("new object names will be checked against pre-import full names inside the transaction", change.OldValue, StringComparison.Ordinal);
        Assert.DoesNotContain(@"D:\Imports", change.OldValue, StringComparison.Ordinal);
        Assert.Equal(("plc/Phoenix1.pbf", "1024", _sha256, "0", "false", "0", "2", "1", "2"),
            (plan.Details!["file"], plan.Details["bytes"], plan.Details["sha256"], plan.Details["importMatch"], plan.Details["preflightComplete"], plan.Details["sourceDeviceCount"], plan.Details["functions"], plan.Details["plcBoxes"], plan.Details["articleReferences"]));
        Assert.Equal(0, _importer.Imports);
        Assert.Empty(_steps);
    }

    [Theory]
    [InlineData("PlcDcXMLExchangerUniversal", "pbf")]
    [InlineData("PlcDcXMLExchangerUniversal", "PBF")]
    [InlineData("PlcDcAMLExchangerGeneral", "aml")]
    [InlineData("PlcDcAMLExchangerGeneral", "AML")]
    public void Plan_OnlyTheTwoVerifiedConverters_AcceptTheirPairedExtension(string converter, string extension)
    {
        ImportPlcArgs args = Args();
        args.Converter = converter;
        args.File = "plc/Phoenix1." + extension;
        args.FilePath = @"D:\Imports\plc\Phoenix1." + extension;

        Assert.Equal(converter, Plan(args).Details!["converter"]);
    }

    [Theory]
    [InlineData("PlcDcXMLExchangerUniversal", "plc/p.aml", @"D:\Imports\p.aml")]
    [InlineData("PlcDcAMLExchangerGeneral", "plc/p.pbf", @"D:\Imports\p.pbf")]
    [InlineData("PlcDcAMLExchangerGeneral", "plc/p.aml", @"D:\Imports\p.pbf")]
    [InlineData("PlcDcXMLExchangerUniversal", "plc/p.aml", @"D:\Imports\p.pbf")]
    [InlineData("plcdcxmlExchangerUniversal", "plc/p.pbf", @"D:\Imports\p.pbf")]
    [InlineData(null, "plc/p.pbf", @"D:\Imports\p.pbf")]
    public void Plan_ConverterOrExtensionMismatch_IsInvalidBeforeReading(string? converter, string file, string path)
    {
        ImportPlcArgs args = Args();
        args.Converter = converter;
        args.File = file;
        args.FilePath = path;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.NotEmpty(error.Metadata!["violations"]);
        Assert.Equal(0, _importer.Reads);
    }

    [Fact]
    public void Plan_ReportsAllArgumentProblems()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new ImportPlcArgs())).Error;

        Assert.Equal(5, error.Metadata!["violations"].Split('\n').Length);
        Assert.Equal(0, _importer.Reads);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100 * 1024 * 1024, true)]
    [InlineData(100 * 1024 * 1024 + 1, false)]
    public void Plan_FileSizeLimit_IsEnforced(long bytes, bool valid)
    {
        _bytes = bytes;

        if (valid)
        {
            Assert.Equal(bytes.ToString(CultureInfo.InvariantCulture), Plan(Args()).Details!["bytes"]);
        }
        else
        {
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error.Reason);
        }
    }

    [Fact]
    public void Execute_FileHashMismatch_IsStaleBeforeBackupOrTransaction()
    {
        ImportPlcArgs args = Planned();
        _hash = new string('b', 64);

        AssertFailure(args, ErrorReason.PlanStale, ExecutionState.NotStarted);
        Assert.Empty(_steps);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("name")]
    [InlineData("category")]
    [InlineData("plc")]
    [InlineData("references")]
    [InlineData("bytes")]
    [InlineData("written")]
    [InlineData("file")]
    public void Execute_FingerprintChange_IsStale(string field)
    {
        ImportPlcArgs args = Planned();
        switch (field)
        {
            case "identity":
                _importer.Functions[0] = new(99, "=F+A2-KE1", "PLCBox", true);
                break;
            case "name":
                _importer.Functions[0] = new(1, "=H+A2-KE1", "PLCBox", true);
                break;
            case "category":
                _importer.Functions[0] = new(1, "=F+A2-KE1", "Other", true);
                break;
            case "plc":
                _importer.Functions[0] = new(1, "=F+A2-KE1", "PLCBox", false);
                break;
            case "references":
                _importer.ArticleReferences++;
                break;
            case "bytes":
                _bytes++;
                break;
            case "written":
                _written++;
                break;
            case "file":
                args.FilePath = @"D:\Imports\other.pbf";
                break;
        }

        AssertFailure(args, ErrorReason.PlanStale, ExecutionState.NotStarted);
        Assert.Empty(_steps);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("file")]
    [InlineData("hash")]
    public void Execute_ChangeDuringBackup_IsStaleBeforeImport(string field)
    {
        ImportPlcArgs args = Planned();
        _duringBackup = () =>
        {
            if (field == "project")
            {
                _importer.Functions.Add(new(9, "=H-K1", "Other", false));
            }
            else if (field == "file")
            {
                _written++;
            }
            else
            {
                _hash = new string('b', 64);
            }
        };

        AssertFailure(args, ErrorReason.PlanStale, ExecutionState.NotStarted);
        Assert.Equal(["Backup"], _steps);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_NewNameMatchingAnOldName_RollsBack_EvenAcrossCategories(bool plc)
    {
        _importer.Functions.Add(new(9, "=F+A2-KE1", "Other", false));
        _importer.Added = [new(3, "=F+A2-KE1", "Other", plc), new(4, "=F+A2-KE1", "Terminal", false)];
        ImportPlcArgs args = Planned();

        BridgeError error = AssertFailure(args, ErrorReason.AlreadyExists, ExecutionState.RolledBack);

        Assert.False(error.Retryable);
        Assert.Equal(("1", "false"), (error.Metadata!["duplicateCount"], error.Metadata["truncated"]));
        JToken duplicate = Assert.Single(JArray.Parse(error.Metadata["duplicates"]));
        Assert.Equal(("=F+A2-KE1", 2), (duplicate.Value<string>("name"), duplicate.Value<int>("existingCount")));
        Assert.Equal(3, _importer.Functions.Count);
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_DifferentNewObjectsWithTheSameName_Commit_AndReportSharingAsInformation(bool plc)
    {
        _importer.Added = [new(3, "=G+A2-KE1", plc ? "PLCBox" : "Other", plc), new(4, "=G+A2-KE1", "Other", false)];

        ImportPlcData result = Execute(Planned());

        Assert.Equal((4, 2, 2), (result.After.Functions, result.AddedFunctions, result.NewObjectsSharingNames));
        Assert.Equal(plc ? 1 : 0, result.AddedPlcBoxes);
        Assert.Equal(4, _importer.ArticleReferences);
        Assert.Contains("Commit", _transactions.Calls);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_SharedNewNames_DoNotAppearInExistingNameConflictMetadata()
    {
        _importer.Added = [new(3, "=F+A2-KE1", "PLCBox", true), new(4, "=G-K1", "Other", false), new(5, "=G-K1", "Other", false)];

        BridgeError error = AssertFailure(Planned(), ErrorReason.AlreadyExists, ExecutionState.RolledBack);

        Assert.Equal(("1", "false"), (error.Metadata!["duplicateCount"], error.Metadata["truncated"]));
        JToken duplicate = Assert.Single(JArray.Parse(error.Metadata["duplicates"]));
        Assert.Equal(("=F+A2-KE1", 1), (duplicate.Value<string>("name"), duplicate.Value<int>("existingCount")));
        Assert.Equal(2, _importer.Functions.Count);
    }

    [Fact]
    public void Execute_MoreThan50DuplicateNames_CountsAllNamesBeforeTruncating()
    {
        _importer.Functions.AddRange(Enumerable.Range(0, 60).Select(i => new PlcImportFunction(1000 + i, "=G-K" + i.ToString(CultureInfo.InvariantCulture), "Other", false)));
        _importer.Added = [.. Enumerable.Range(0, 60).SelectMany(i => new[]
        {
            new PlcImportFunction(100 + i * 2, "=G-K" + i.ToString(CultureInfo.InvariantCulture), "Other", false),
            new PlcImportFunction(101 + i * 2, "=G-K" + i.ToString(CultureInfo.InvariantCulture), "Other", false),
        })];

        BridgeError error = AssertFailure(Planned(), ErrorReason.AlreadyExists, ExecutionState.RolledBack);

        Assert.Equal(("60", "true", 50), (error.Metadata!["duplicateCount"], error.Metadata["truncated"], JArray.Parse(error.Metadata["duplicates"]).Count));
        Assert.All(JArray.Parse(error.Metadata["duplicates"]), duplicate => Assert.Equal(1, duplicate.Value<int>("existingCount")));
        Assert.Equal(62, _importer.Functions.Count);
    }

    [Fact]
    public void Execute_MoreThan50SharedNewNames_Commit_AndCountAllSharingObjects()
    {
        _importer.Added = [.. Enumerable.Range(0, 60).SelectMany(i => new[]
        {
            new PlcImportFunction(100 + i * 2, "=G-K" + i.ToString(CultureInfo.InvariantCulture), "Other", false),
            new PlcImportFunction(101 + i * 2, "=G-K" + i.ToString(CultureInfo.InvariantCulture), "Other", false),
        })];

        ImportPlcData result = Execute(Planned());

        Assert.Equal((120, 120, 122), (result.NewObjectsSharingNames, result.AddedFunctions, result.After.Functions));
        Assert.Contains("Commit", _transactions.Calls);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_ExistingDuplicateNamesAlone_AreAllowed_AndOverlapByIdentityCountsOnce()
    {
        _importer.Functions.Add(new(9, "=F+A2-KE1", "Other", false));
        _importer.Added.Add(_importer.Added[0]);

        ImportPlcData result = Execute(Planned());

        Assert.Equal((3, 5, 2, 1), (result.Before.Functions, result.After.Functions, result.AddedFunctions, result.AddedPlcBoxes));
        Assert.Equal(0, result.NewObjectsSharingNames);
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_ExactFullNames_DistinguishStructureAndCase()
    {
        _importer.Added = [new(3, "=f+A2-KE1", "PLCBox", true), new(4, "=G+A2-KE1", "PLCBox", true)];

        Assert.Equal(2, Execute(Planned()).AddedPlcBoxes);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("rename")]
    [InlineData("box")]
    [InlineData("references")]
    public void Execute_ExistingObjectsRemovedOrChanged_RollsBackWithOperationFailed(string change)
    {
        _importer.DuringImport = () =>
        {
            switch (change)
            {
                case "remove":
                    _importer.Functions.RemoveAt(0);
                    break;
                case "rename":
                    _importer.Functions[0] = new(1, "=X-KE1", "PLCBox", true);
                    break;
                case "box":
                    _importer.Functions[0] = new(1, "=F+A2-KE1", "PLCBox", false);
                    break;
                case "references":
                    _importer.ArticleReferences = 0;
                    break;
            }
        };

        AssertFailure(Planned(), ErrorReason.EplanOperationFailed, ExecutionState.RolledBack);
        Assert.Equal((2, "=F+A2-KE1", 2), (_importer.Functions.Count, _importer.Functions[0].Name, _importer.ArticleReferences));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_ActionFalseOrException_RollsBackPartialChanges(bool throws)
    {
        _importer.Returned = false;
        if (throws)
        {
            _importer.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "The action failed.");
        }

        AssertFailure(Planned(), ErrorReason.EplanOperationFailed, ExecutionState.RolledBack);
        Assert.Equal(2, _importer.Functions.Count);
    }

    [Fact]
    public void Execute_ReadBackFailure_RollsBack()
    {
        _importer.DuringImport = () => _importer.ReadFailureAt = _importer.Reads + 1;

        AssertFailure(Planned(), ErrorReason.EplanOperationFailed, ExecutionState.RolledBack);
        Assert.Equal(2, _importer.Functions.Count);
    }

    [Fact]
    public void Execute_Success_ReportsAllCountsAndAddedCounts_AfterBackupAndOneTransaction()
    {
        ImportPlcData result = Execute(Planned());

        Assert.Equal(("Line1", "plc/Phoenix1.pbf", 1024, _sha256, "PlcDcXMLExchangerUniversal"), (result.Project, result.File, result.Bytes, result.Sha256, result.Converter));
        Assert.Equal((2, 1, 2), (result.Before.Functions, result.Before.PlcBoxes, result.Before.ArticleReferences));
        Assert.Equal((4, 2, 4), (result.After.Functions, result.After.PlcBoxes, result.After.ArticleReferences));
        Assert.Equal((2, 1, null), (result.AddedFunctions, result.AddedPlcBoxes, result.Warning));
        Assert.Equal(0, result.NewObjectsSharingNames);
        Assert.Equal((_project.Path, @"D:\Imports\plc\Phoenix1.pbf", result.Converter), _importer.Arguments);
        Assert.Equal(["Backup", "Begin"], _steps);
        Assert.Equal(["Begin AI: eplan_import_plc", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("backup")]
    [InlineData("action")]
    [InlineData("readback")]
    public void Execute_Cancellation_StopsAtTheNextSafePoint(string when)
    {
        ImportPlcArgs args = Planned();
        var context = new OperationContext("plc-cancel", write: true, progress: null);
        if (when == "before")
        {
            context.Cancel();
        }
        else if (when == "backup")
        {
            _duringBackup = context.Cancel;
        }
        else if (when == "action")
        {
            _importer.DuringImport = context.Cancel;
        }
        else
        {
            _importer.OnRead = () => { if (_importer.Imports > 0) { context.Cancel(); } };
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(args))).Error;

        Assert.Equal((ErrorReason.Cancelled, when is "before" or "backup" ? ExecutionState.NotStarted : ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(2, _importer.Functions.Count);
        Assert.DoesNotContain("Commit", _transactions.Calls);
        Assert.Equal(when is "before" or "backup" ? 0 : 1, _importer.Imports);
    }

    [Theory]
    [InlineData("Rollback")]
    [InlineData("restore")]
    [InlineData("read")]
    public void Execute_RollbackFailureOrUnverifiableBaseline_IsUnknown(string failure)
    {
        _importer.Returned = false;
        if (failure == "Rollback")
        {
            _transactions.FailAt = "Rollback";
        }
        else if (failure == "restore")
        {
            _transactions.Snapshot = () => () => { };
        }
        else
        {
            _importer.DuringImport = () => _importer.ReadFailureAt = _importer.Reads + 1;
        }

        AssertFailure(Planned(), ErrorReason.OutcomeUnknown, ExecutionState.Unknown);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_BackupFailure_DoesNotImport()
    {
        _duringBackup = () => throw new BridgeErrorException(ErrorReason.BackupFailed, "The backup failed.");

        AssertFailure(Planned(), ErrorReason.BackupFailed, ExecutionState.NotStarted);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutFingerprint_DoesNotReadOrBackUp()
    {
        AssertFailure(Args(), ErrorReason.InvalidArgument, ExecutionState.NotStarted);
        Assert.Equal(0, _importer.Reads);
        Assert.Empty(_steps);
    }

    private static ImportPlcArgs Args() => new() { Project = "Line1", File = "plc/Phoenix1.pbf", FilePath = @"D:\Imports\plc\Phoenix1.pbf", Sha256 = _sha256, Converter = "PlcDcXMLExchangerUniversal" };

    private ImportPlcArgs Planned()
    {
        ImportPlcArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        return args;
    }

    private void Read(ImportSource source) => source.Observe(_bytes, _written, _hash);

    private WritePlan Plan(ImportPlcArgs args) => (WritePlan)new ImportPlcPlanOperation(_catalog, _importer, Read).Execute(args)!;

    private ImportPlcData Execute(ImportPlcArgs args) => (ImportPlcData)new ImportPlcExecuteOperation(_catalog, _pages, _importer, _backup, _transactions, Read, (_, _) =>
    {
        _steps.Add("Backup");
        _duringBackup?.Invoke();
    }).Execute(args)!;

    private BridgeError AssertFailure(ImportPlcArgs args, string reason, string execution)
    {
        int imports = _importer.Imports;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((reason, execution), (error.Reason, error.Execution));
        Assert.False(error.Retryable);
        if (execution == ExecutionState.NotStarted)
        {
            Assert.Equal(imports, _importer.Imports);
        }

        return error;
    }

    private sealed class Importer : IPlcImporter
    {
        public List<PlcImportFunction> Functions { get; set; } = [];
        public List<PlcImportFunction> Added { get; set; } = [];
        public int ArticleReferences { get; set; } = 2;
        public int Imports { get; private set; }
        public int Reads { get; private set; }
        public int ReadFailureAt { get; set; } = -1;
        public bool Returned { get; set; } = true;
        public BridgeErrorException? Failure { get; set; }
        public Action? DuringImport { get; set; }
        public Action? OnRead { get; set; }
        public (string Project, string File, string Converter) Arguments { get; private set; }

        public PlcImportState Read(string projectPath)
        {
            Reads++;
            OnRead?.Invoke();
            if (Reads == ReadFailureAt)
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The required read failed.");
            }

            return new PlcImportState(Functions, new ImportPlcCounts { Functions = Functions.Select(f => f.Id).Distinct().Count(), PlcBoxes = Functions.Where(f => f.IsPlcBox).Select(f => f.Id).Distinct().Count(), ArticleReferences = ArticleReferences });
        }

        public bool Import(string projectPath, string filePath, string converter)
        {
            Imports++;
            Arguments = (projectPath, filePath, converter);
            Functions.AddRange(Added);
            ArticleReferences += Added.Count;
            DuringImport?.Invoke();
            return Failure is null ? Returned : throw Failure;
        }
    }
}

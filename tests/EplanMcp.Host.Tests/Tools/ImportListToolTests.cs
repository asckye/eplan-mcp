using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>SEC-31 and the Destructive list import pipeline against a fake Bridge, with real files and audit.</summary>
[Trait("Size", "Medium")]
public sealed class ImportListToolTests : IDisposable
{
    private const string File = "incoming/list.xml";
    private readonly WriteToolHarness _harness;
    private readonly FakeApprovalChannel _channel;
    private readonly ImportFiles _files;
    private readonly string _path;
    private readonly RecordedProgress _progress = new();
    private ApprovalDecision _decision = ApprovalDecision.Granted;
    private Action? _whileApproving;
    private string? _failure;
    private readonly ImportListData _data = new()
    {
        Project = "Line1",
        Kind = "devices",
        Converter = "XDLXmlExporter",
        Before = new ListImportCounts { ProjectParts = 293, ArticleReferences = 2900 },
        After = new ListImportCounts { DeviceListEntries = 2, ProjectParts = 293, ArticleReferences = 2900 },
    };

    public ImportListToolTests()
    {
        _channel = new("eplan_dialog", null)
        {
            Answers = question => question.Release is null ? _decision : ApprovalDecision.Denied,
            Wait = () => _whileApproving?.Invoke(),
        };
        _harness = new(_channel);
        _files = new(_harness.Locations);
        _path = Path.Combine(_files.Root, "incoming", "list.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        System.IO.File.WriteAllText(_path, "<deviceList count=\"0\"/>"); // Well-formed XML; the fake does not parse EPLAN rows.
        _harness.Bridge.Handlers[OperationNames.ImportListPlan] = args =>
        {
            var given = (ImportListArgs)args!;
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "update", Kind = given.Kind!, Name = "Line1", Property = "list" }]);
            plan.Details = new()
            {
                ["kind"] = given.Kind!,
                ["file"] = given.File!,
                ["sha256"] = given.Sha256!,
                ["bytes"] = new FileInfo(given.FilePath!).Length.ToString(CultureInfo.InvariantCulture),
                ["converter"] = given.Kind == "parts" ? "XPalXmlExporter" : given.Format == "csv" ? "XDLCsvImporterExporter" : "XDLXmlExporter",
                ["before"] = JsonSerializer.Serialize(_data.Before, HostJson.Options),
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ImportListExecute] = args =>
        {
            var given = (ImportListArgs)args!;
            if (_failure != ErrorReason.BackupFailed)
            {
                System.IO.File.WriteAllText(given.Backup!.Path, "snapshot");
            }

            if (_failure is { } reason)
            {
                throw new BridgeCallException(new BridgeError
                {
                    Reason = reason,
                    Message = "List import failed.",
                    Execution = reason is ErrorReason.PlanStale or ErrorReason.BackupFailed ? ExecutionState.NotStarted
                        : reason == ErrorReason.OutcomeUnknown ? ExecutionState.Unknown : ExecutionState.RolledBack
                });
            }

            return _data;
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DryRun_HasFileConverterAndCounts_WithoutApprovalBackupOrAction()
    {
        CallToolResult result = await Import(dryRun: true);
        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, File, Hash(), "XDLXmlExporter"),
            (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("file").GetString(), data.GetProperty("sha256").GetString(), data.GetProperty("converter").GetString()));
        Assert.Equal(2900, data.GetProperty("before").GetProperty("articleReferences").GetInt32());
        foreach (string property in new[] { "after", "backup", "undo" })
        {
            Assert.Equal(JsonValueKind.Null, data.GetProperty(property).ValueKind);
        }

        Assert.Equal("Dry run: would import a list into the project; row-level effects are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_channel.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task Scratch_AlwaysAsks_ThenPassesTheApprovedFileAnd150SecondBackupWithin300Seconds()
    {
        _harness.Bridge.DuringWrite = progress => progress!.Report(new ProgressNotification { Message = "Importing project list", Progress = 1 });
        CallToolResult result = await Import();
        JsonElement data = WriteToolHarness.Structured(result);
        var sent = Assert.IsType<ImportListArgs>(_harness.Bridge.LastArgs);
        Assert.Equal((_path, Hash(), "f1", 150), (sent.FilePath, sent.Sha256, sent.Fingerprint, sent.Backup!.MaxSeconds));
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.Equal(("committed", 2, sent.Backup.Path, JsonValueKind.Null),
            (data.GetProperty("execution").GetString(), data.GetProperty("after").GetProperty("deviceListEntries").GetInt32(), data.GetProperty("backup").GetString(), data.GetProperty("undo").ValueKind));
        Assert.True(System.IO.File.Exists(sent.Backup.Path));
        ApprovalQuestion question = Assert.Single(_channel.Questions);
        ApprovalAskArgs text = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));
        Assert.Contains(File, text.Changes[0], StringComparison.Ordinal);
        Assert.Contains(Hash(), text.Changes[0], StringComparison.Ordinal);
        Assert.Contains("Converter: XDLXmlExporter", text.Changes[0], StringComparison.Ordinal);
        Assert.Contains("Undo after commit has not been verified", text.Note, StringComparison.Ordinal);
        Assert.Contains(_progress.Values, p => p.Message == "Importing project list");
        Assert.Contains(Hash(), Audit(), StringComparison.Ordinal);
        Assert.Contains("converter XDLXmlExporter", Audit(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("devices", "xml", "XDLXmlExporter", 2900)]
    [InlineData("devices", "csv", "XDLCsvImporterExporter", 2900)]
    [InlineData("parts", "xml", "XPalXmlExporter", 2902)]
    public async Task EachSupportedFormat_PreservesItsScopeConverterAndBusinessCounts(string kind, string format, string converter, int references)
    {
        string relative = "incoming/list." + format;
        if (format == "csv")
        { System.IO.File.WriteAllText(Path.ChangeExtension(_path, ".csv"), "sample for fake importer"); }
        _data.Kind = kind;
        _data.Converter = converter;
        _data.After.ArticleReferences = references;
        JsonElement data = WriteToolHarness.Structured(await ListImportTools.ImportListAsync(_harness.Pipeline, _files, "Line1", kind, relative, format, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((kind, converter, references), (data.GetProperty("kind").GetString(), data.GetProperty("converter").GetString(), data.GetProperty("after").GetProperty("articleReferences").GetInt32()));
        var sent = Assert.IsType<ImportListArgs>(_harness.Bridge.LastArgs);
        Assert.Equal((kind, format), (sent.Kind, sent.Format));
        Assert.False(data.TryGetProperty("importedRows", out _));
    }

    [Fact]
    public async Task CsvExactly100Mb_IsAcceptedWithoutInventingItsRowEffects()
    {
        string path = Path.ChangeExtension(_path, ".csv");
        using (FileStream stream = System.IO.File.Create(path))
        { stream.SetLength(ListImportTools.MaxBytes); }
        JsonElement data = WriteToolHarness.Structured(await ListImportTools.ImportListAsync(_harness.Pipeline, _files, "Line1", "devices", "incoming/list.csv", "csv", dry_run: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ListImportTools.MaxBytes, data.GetProperty("bytes").GetInt64());
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task Denial_DoesNotSendExecuteOrCreateBackup()
    {
        _decision = ApprovalDecision.Denied;
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Theory]
    [InlineData(ErrorReason.BackupFailed, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)] // action false
    [InlineData(ErrorReason.Cancelled, ExecutionState.RolledBack)]
    public async Task BridgeFailure_PreservesExecutionStateAndRetainsAnySnapshot(string reason, string execution)
    {
        _failure = reason;
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        if (execution == ExecutionState.RolledBack)
        {
            Assert.True(System.IO.File.Exists(error.GetProperty("metadata").GetProperty("backup").GetString()));
        }
        Assert.Contains("\"execution\":\"" + execution + "\"", Audit(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("time")]
    [InlineData("missing")]
    public async Task FileChangedDuringApproval_BridgeReportsNotStarted(string change)
    {
        string approvedHash = Hash();
        string reason = change == "missing" ? ErrorReason.FileNotFound : ErrorReason.PlanStale;
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = reason,
            Execution = ExecutionState.NotStarted,
            Message = "The import file changed or is no longer available.",
        };
        _whileApproving = () =>
        {
            if (change == "missing")
            { System.IO.File.Delete(_path); }
            else if (change == "time")
            { System.IO.File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(1)); }
            else
            { System.IO.File.WriteAllText(_path, "<other/>"); }
        };
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        Assert.Single(_harness.Bridge.Writes);
        Assert.Equal(approvedHash, Assert.IsType<ImportListArgs>(_harness.Bridge.LastArgs).Sha256);
        Assert.Contains("execution_result", Audit(), StringComparison.Ordinal);
        Assert.Empty(SuspensionStore.For(_harness.Locations).All());
    }

    [Fact]
    public async Task ReadbackFailure_ReturnsRolledBackWithMessages()
    {
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = ErrorReason.EplanOperationFailed,
            Execution = ExecutionState.RolledBack,
            Message = "Readback failed.",
            Metadata = new() { ["eplanMessages"] = """[{"level":"error","number":"S000123","text":"Bad\u0007 row","occurrences":1}]""" }
        };
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal(ExecutionState.RolledBack, error.GetProperty("execution").GetString());
        Assert.Contains("Bad row", error.GetProperty("metadata").GetProperty("eplanMessages").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledDuringApproval_PropagatesWithoutSendingExecute()
    {
        using var cancel = new CancellationTokenSource();
        _whileApproving = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ListImportTools.ImportListAsync(_harness.Pipeline, _files, "Line1", "devices", File, cancellationToken: cancel.Token));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task RecoveredCommit_IsNotRepeated_AndDoesNotInventAfterCounts()
    {
        _harness.Bridge.WriteRecovered = true;
        JsonElement data = WriteToolHarness.Structured(await Import());
        Assert.Equal((true, "committed", JsonValueKind.Null), (data.GetProperty("recovered").GetBoolean(), data.GetProperty("execution").GetString(), data.GetProperty("after").ValueKind));
        Assert.Single(_harness.Bridge.Writes);
        Assert.Contains("unavailable", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownOutcome_SuspendsProjectAndInstance_AndKeepsBackup()
    {
        _failure = ErrorReason.OutcomeUnknown;
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.True(System.IO.File.Exists(error.GetProperty("metadata").GetProperty("backup").GetString()));
        IReadOnlyList<Suspension> suspended = SuspensionStore.For(_harness.Locations).All();
        Assert.Equal(2, suspended.Count);
        Assert.Contains(suspended, s => s.Project is not null);
        Assert.Contains(suspended, s => s.ProcessId == Environment.ProcessId);
    }

    [Fact]
    public async Task NoCountChange_StillCommitsWithWarningAndWithoutUndoClaim()
    {
        _data.After = _data.Before;
        _data.Warning = "Committed with a warning.";
        JsonElement data = WriteToolHarness.Structured(await Import());
        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.Contains("No count changes were observed", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Contains("Committed with a warning", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("undo").ValueKind);
    }

    [Fact]
    public async Task Messages_AreCleanedLimitedAndFitTheBudget()
    {
        _data.EplanMessages = [.. Enumerable.Range(0, 30).Select(_ => new SystemMessage { Text = "\u0007\u202e" + new string('字', 600), Occurrences = 1 })];
        _data.EplanMessageCount = 30;
        JsonElement data = WriteToolHarness.Structured(await Import());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.InRange(data.GetProperty("eplanMessages").GetArrayLength(), 1, 20);
        Assert.InRange(ResultShaper.EstimateTokens(data.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
        Assert.Contains("of 30 EPLAN messages", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.All(data.GetProperty("eplanMessages").EnumerateArray(), m => Assert.DoesNotContain('\u0007', m.GetProperty("text").GetString()!));
    }

    [Theory]
    [InlineData("<bad>")]
    [InlineData("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///secret'>]><a>&x;</a>")]
    [InlineData("")]
    public async Task InvalidXmlDtdOrEmptyFile_NeverReachesBridge(string xml)
    {
        System.IO.File.WriteAllText(_path, xml);
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
        Assert.DoesNotContain(_harness.Locations.Root, error.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileLargerThan100Mb_IsRefusedBeforeHashingOrPlanning()
    {
        using (FileStream stream = System.IO.File.OpenWrite(_path))
        { stream.SetLength(ListImportTools.MaxBytes + 1); }
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Theory]
    [InlineData("parts", "csv", "list.csv")]
    [InlineData("other", "xml", "../list.xml")]
    [InlineData("devices", "yaml", "list.xml")]
    [InlineData("devices", "xml", "list.csv")]
    public async Task InvalidArguments_AreRejectedBeforeFileAccess(string kind, string format, string file)
    {
        JsonElement error = WriteToolHarness.Error(await ListImportTools.ImportListAsync(_harness.Pipeline, _files, "Line1", kind, file, format, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.NotEmpty(error.GetProperty("metadata").GetProperty("violations").GetString()!);
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Import(bool dryRun = false) => ListImportTools.ImportListAsync(_harness.Pipeline, _files, "Line1", "devices", File, dry_run: dryRun, progress: _progress, cancellationToken: TestContext.Current.CancellationToken);
    private string Hash() => Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(_path)));
    private string Audit() => string.Join("\n", Directory.EnumerateFiles(_harness.Locations.Audit).Select(System.IO.File.ReadAllText));
}

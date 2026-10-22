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

/// <summary>The real destructive pipeline, safe XML and SEC-31 files against a fake Bridge; no native imports.</summary>
[Trait("Size", "Medium")]
public sealed class ImportPlcToolTests : IDisposable
{
    private const string FileName = "plc/Phoenix1.pbf";
    private readonly FakeApprovalChannel _dialog;
    private readonly WriteToolHarness _harness;
    private readonly ImportFiles _files;
    private readonly string _path;
    private readonly string _sha256;
    private Action _duringApproval = () => { };
    private ApprovalDecision _decision = ApprovalDecision.Granted;
    private readonly ImportPlcData _data = new()
    {
        Project = "Line1",
        Before = new() { Functions = 2876, PlcBoxes = 20, ArticleReferences = 2900 },
        After = new() { Functions = 2880, PlcBoxes = 22, ArticleReferences = 2904 },
        AddedFunctions = 4,
        AddedPlcBoxes = 2,
        NewObjectsSharingNames = 2,
        EplanMessages = [new() { Level = "warning", Number = "S123456", Text = "Native import warning.", Occurrences = 1 }],
        EplanMessageCount = 1,
    };

    public ImportPlcToolTests()
    {
        _dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted) { Wait = () => _duringApproval(), Answers = _ => _decision };
        _harness = new WriteToolHarness(OutsideScratchWrites.Deny, _dialog);
        _files = new ImportFiles(_harness.Locations);
        _path = Path.Combine(_files.Root, "plc", "Phoenix1.pbf");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        System.IO.File.WriteAllText(_path, "<Unknown xmlns='urn:unverified'><DeviceName>=F+A2-KE1</DeviceName></Unknown>");
        _sha256 = Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(_path)));
        _harness.Bridge.Handlers[OperationNames.ImportPlcPlan] = args =>
        {
            var sent = (ImportPlcArgs)args!;
            WritePlan plan = _harness.Plan([new() { Action = "update", Kind = "project", Property = "PLC bus data", Name = "Line1", OldValue = "PLC file; file preflight is incomplete" }]);
            plan.Details = new() { ["file"] = sent.File!, ["bytes"] = new FileInfo(sent.FilePath!).Length.ToString(CultureInfo.InvariantCulture), ["sha256"] = sent.Sha256!, ["converter"] = sent.Converter!, ["sourceDeviceCount"] = "0", ["functions"] = "2876", ["plcBoxes"] = "20", ["articleReferences"] = "2900", ["preflightComplete"] = "false" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ImportPlcExecute] = args =>
        {
            System.IO.File.WriteAllText(((ImportPlcArgs)args!).Backup!.Path, "copy");
            return _data;
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DryRun_UnknownNamesRemainIncomplete_WithoutApprovalBackupOrExecute()
    {
        JsonElement data = WriteToolHarness.Structured(await Import(dryRun: true));

        Assert.Equal((ExecutionState.NotStarted, true, FileName, _sha256), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("file").GetString(), data.GetProperty("sha256").GetString()));
        Assert.Equal(2876, data.GetProperty("before").GetProperty("functions").GetInt32());
        JsonElement preflight = data.GetProperty("preflight");
        Assert.False(preflight.GetProperty("complete").GetBoolean());
        Assert.True(preflight.GetProperty("canExecute").GetBoolean());
        Assert.Equal(0, preflight.GetProperty("sourceDeviceCount").GetInt32());
        Assert.Empty(preflight.GetProperty("duplicates").EnumerateArray());
        foreach (string field in new[] { "after", "addedFunctions", "addedPlcBoxes", "newObjectsSharingNames", "backup", "undo" })
        {
            Assert.Equal(JsonValueKind.Null, data.GetProperty(field).ValueKind);
        }

        Assert.Contains("preflight is incomplete", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Theory]
    [InlineData("PlcDcXMLExchangerUniversal", "pbf")]
    [InlineData("PlcDcXMLExchangerUniversal", "PBF")]
    [InlineData("PlcDcAMLExchangerGeneral", "AML")]
    public async Task Commit_ApprovedInScratch_WithBackupFileHashBudgetAndUnverifiedUndo(string converter, string extension)
    {
        string file = "plc/copy." + extension;
        string full = Path.Combine(_files.Root, file.Replace('/', Path.DirectorySeparatorChar));
        System.IO.File.Copy(_path, full);

        JsonElement data = WriteToolHarness.Structured(await PlcImportTools.ImportPlcAsync(_harness.Pipeline, _files, "Line1", file, converter, cancellationToken: TestContext.Current.CancellationToken));

        var planned = (ImportPlcArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.ImportPlcPlan).Args!;
        var executed = (ImportPlcArgs)_harness.Bridge.LastArgs!;
        Assert.Equal((full, _sha256, file, converter), (planned.FilePath, planned.Sha256, planned.File, planned.Converter));
        Assert.Equal((full, _sha256, "f1", 150), (executed.FilePath, executed.Sha256, executed.Fingerprint, executed.Backup!.MaxSeconds));
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.Empty(_harness.Bridge.Timeouts);
        Assert.Single(_dialog.Questions);
        Assert.Equal(ExecutionState.Committed, data.GetProperty("execution").GetString());
        Assert.Equal((2880, 22, 2904), (data.GetProperty("after").GetProperty("functions").GetInt32(), data.GetProperty("after").GetProperty("plcBoxes").GetInt32(), data.GetProperty("after").GetProperty("articleReferences").GetInt32()));
        Assert.Equal((4, 2, 2), (data.GetProperty("addedFunctions").GetInt32(), data.GetProperty("addedPlcBoxes").GetInt32(), data.GetProperty("newObjectsSharingNames").GetInt32()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("undo").ValueKind);
        Assert.True(System.IO.File.Exists(data.GetProperty("backup").GetString()));
        Assert.Single(data.GetProperty("eplanMessages").EnumerateArray());
        Assert.False(data.GetProperty("preflight").GetProperty("complete").GetBoolean());
        string audit = string.Join("\n", Directory.EnumerateFiles(_harness.Locations.Audit).Select(System.IO.File.ReadAllText));
        Assert.Contains(_sha256, audit, StringComparison.Ordinal);
        Assert.Contains("import PLC bus data:", audit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pbf", "PlcDcAMLExchangerGeneral")]
    [InlineData("aml", "PlcDcXMLExchangerUniversal")]
    [InlineData("pbf", "unknown")]
    public async Task ConverterMismatch_IsInvalidBeforeBridge(string extension, string converter)
    {
        JsonElement error = WriteToolHarness.Error(await PlcImportTools.ImportPlcAsync(_harness.Pipeline, _files, "Line1", "plc/file." + extension, converter, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Theory]
    [InlineData("../secret.pbf")]
    [InlineData("C:/secret.pbf")]
    [InlineData("//server/share.pbf")]
    [InlineData("plc/CON.pbf")]
    public async Task UnsafePaths_AndMissingProject_AreReportedTogether(string file)
    {
        JsonElement error = WriteToolHarness.Error(await PlcImportTools.ImportPlcAsync(_harness.Pipeline, _files, " ", file, PlcImportTools.Pbf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, error.GetProperty("metadata").GetProperty("violations").GetString()!.Split('\n').Length);
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100 * 1024 * 1024 + 1)]
    public async Task EmptyOrOversizedFile_IsInvalidBeforeBridge(long size)
    {
        using (var stream = System.IO.File.OpenWrite(_path))
        { stream.SetLength(size); }
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Fact]
    public async Task MissingFile_IsNotFoundWithoutLocalPaths()
    {
        System.IO.File.Delete(_path);
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((ErrorReason.FileNotFound, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.DoesNotContain(_harness.Locations.Root, error.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Theory]
    [InlineData("<broken>")]
    [InlineData("<!DOCTYPE root [<!ENTITY text 'injected'>]><root>&text;</root>")]
    [InlineData("<!DOCTYPE root SYSTEM 'file:///secret'><root/>")]
    [InlineData("<root xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='https://example.invalid'/></root>")]
    public async Task UnsafeOrMalformedXml_IsInvalidBeforeBridge(string xml)
    {
        System.IO.File.WriteAllText(_path, xml);
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.DoesNotContain("file:///secret", error.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Fact]
    public async Task Approval_ShowsFullHashSizeConverterRiskCountsAndNoUndoPromise()
    {
        await Import();
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        ApprovalAskArgs text = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));
        string change = Assert.Single(text.Changes);
        Assert.Contains($"Import PLC bus data from {FileName} ({new FileInfo(_path).Length} bytes, SHA-256 {_sha256})", change, StringComparison.Ordinal);
        Assert.Contains("This imports new devices; matching updates are not guaranteed.", change, StringComparison.Ordinal);
        Assert.Contains("File preflight is incomplete", change, StringComparison.Ordinal);
        Assert.Contains("2876 functions, 20 PLC boxes, 2900 article references", change, StringComparison.Ordinal);
        Assert.Contains("Undo after commit has not been verified", text.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("undoes the step", text.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("time")]
    [InlineData("missing")]
    public async Task FileChangedDuringApproval_IsRejectedByBridgeBeforeBackup(string change)
    {
        DateTime written = System.IO.File.GetLastWriteTimeUtc(_path);
        _duringApproval = () =>
        {
            if (change == "missing")
            { System.IO.File.Delete(_path); }
            else if (change == "time")
            { System.IO.File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(1)); }
            else
            { System.IO.File.WriteAllText(_path, "<changed/>"); }
        };
        _harness.Bridge.Handlers[OperationNames.ImportPlcExecute] = args =>
        {
            var sent = (ImportPlcArgs)args!;
            var current = new FileInfo(sent.FilePath!);
            Assert.False(System.IO.File.Exists(sent.Backup!.Path));
            Assert.True(!current.Exists || current.LastWriteTimeUtc != written
                || Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(sent.FilePath!))) != sent.Sha256);
            throw new BridgeCallException(new BridgeError
            {
                Reason = current.Exists ? ErrorReason.PlanStale : ErrorReason.FileNotFound,
                Execution = ExecutionState.NotStarted,
                Message = "The fake Bridge rejected the changed import file before backup.",
            });
        };
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((change == "missing" ? ErrorReason.FileNotFound : ErrorReason.PlanStale, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal("The fake Bridge rejected the changed import file before backup.", error.GetProperty("message").GetString());
        Assert.Single(_harness.Bridge.Writes);
        Assert.Equal(_sha256, ((ImportPlcArgs)_harness.Bridge.LastArgs!).Sha256);
        Assert.Empty(Directory.EnumerateFiles(_harness.Locations.Backups, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PolicyDenial_DoesNotAskOrExecute()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "outside", "Line1");
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal(ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task RejectedApproval_DoesNotBackUpOrExecute()
    {
        _decision = ApprovalDecision.Denied;
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Single(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Theory]
    [InlineData(ErrorReason.BackupFailed, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.Cancelled, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)]
    public async Task ExecutionErrors_KeepTheirOutcomeAndDoNotRetry(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Execution = execution, Message = "m", Metadata = new() { ["changed"] = "\u202e" + new string('c', 600) } };
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Single(_harness.Bridge.Writes);
        Assert.DoesNotContain("\u202e", error.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("truncated", error.GetProperty("metadata").GetProperty("changed").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransactionConflict_ReachesModelWithDistinctNamesCountsBackupAndMessages()
    {
        _harness.Bridge.Handlers[OperationNames.ImportPlcExecute] = args =>
        {
            System.IO.File.WriteAllText(((ImportPlcArgs)args!).Backup!.Path, "copy");
            throw new BridgeCallException(new BridgeError { Reason = ErrorReason.AlreadyExists, Execution = ExecutionState.RolledBack, Message = "Import rolled back.", Metadata = new() { ["duplicates"] = "[{\"name\":\"=F+A2-KE1\",\"existingCount\":2}]", ["duplicateCount"] = "1", ["truncated"] = "false", ["eplanMessages"] = "[{\"level\":\"warning\",\"number\":null,\"text\":\"Untrusted\\u202e text\",\"occurrences\":1}]" } });
        };
        JsonElement error = WriteToolHarness.Error(await Import());
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal("1", error.GetProperty("metadata").GetProperty("duplicateCount").GetString());
        Assert.True(System.IO.File.Exists(error.GetProperty("metadata").GetProperty("backup").GetString()));
        Assert.DoesNotContain("\\u202e", error.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using JsonDocument names = JsonDocument.Parse(error.GetProperty("metadata").GetProperty("duplicates").GetString()!);
        Assert.Equal("=F+A2-KE1", names.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task RecoveredCommit_LeavesReadbackUnknownAndNeverRetries()
    {
        _harness.Bridge.WriteRecovered = true;
        JsonElement data = WriteToolHarness.Structured(await Import());
        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("after").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("undo").ValueKind);
        Assert.Contains("response was lost", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Single(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task Messages_AreCleanedAndFitTheBudget_WithTheirTotalKeptInTheHint()
    {
        _data.EplanMessages = [.. Enumerable.Range(0, 20).Select(_ => new SystemMessage { Text = new string('中', 600) + "\u202e", Occurrences = 1 })];
        _data.EplanMessageCount = 25;
        JsonElement data = WriteToolHarness.Structured(await Import());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Contains("25 messages", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\u202e", data.GetRawText(), StringComparison.Ordinal);
        Assert.InRange(ResultShaper.ResultTokens(data.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
    }

    [Fact]
    public async Task ConflictMetadata_IsCleanedAndFitsTheBudget_WithoutLosingTheTotal()
    {
        var duplicates = Enumerable.Range(0, 60).Select(i => new { name = new string('中', 600) + "\u202e" + i.ToString(CultureInfo.InvariantCulture), existingCount = 1 });
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.AlreadyExists, Execution = ExecutionState.RolledBack, Message = "Import rolled back.", Metadata = new() { ["duplicates"] = JsonSerializer.Serialize(duplicates, HostJson.Options), ["duplicateCount"] = "60", ["truncated"] = "false" } };
        JsonElement error = WriteToolHarness.Error(await Import());
        JsonElement metadata = error.GetProperty("metadata");
        Assert.Equal(("60", "true"), (metadata.GetProperty("duplicateCount").GetString(), metadata.GetProperty("truncated").GetString()));
        using JsonDocument names = JsonDocument.Parse(metadata.GetProperty("duplicates").GetString()!);
        Assert.InRange(names.RootElement.GetArrayLength(), 1, 50);
        Assert.DoesNotContain("\u202e", error.GetRawText(), StringComparison.Ordinal);
        Assert.InRange(ResultShaper.ResultTokens(error.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
    }

    [Fact]
    public async Task Progress_UsesTheLongImportActivityAndTheBridgesReports()
    {
        var progress = new RecordedProgress();
        _harness.Bridge.DuringWrite = lane => lane?.Report(new ProgressNotification { Progress = 1, Message = "Reading back PLC identities and counts" });
        await PlcImportTools.ImportPlcAsync(_harness.Pipeline, _files, "Line1", FileName, PlcImportTools.Pbf, progress: progress, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(progress.Values, p => p.Message?.Contains("PLC", StringComparison.Ordinal) == true);
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    private Task<CallToolResult> Import(bool dryRun = false) => PlcImportTools.ImportPlcAsync(_harness.Pipeline, _files, "Line1", FileName, PlcImportTools.Pbf, dryRun, cancellationToken: TestContext.Current.CancellationToken);
}

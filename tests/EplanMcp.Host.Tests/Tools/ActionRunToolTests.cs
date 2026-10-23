using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ActionRunToolTests : IDisposable
{
    private readonly WriteToolHarness _harness;
    private readonly FakeApprovalChannel _dialog;
    private bool _approved = true;
    private readonly ActionRunCatalog _catalog = new();
    private readonly RecordedProgress _progress = new();
    private string _mode = "success";
    private bool _changed;
    private bool _manyFiles;

    public ActionRunToolTests()
    {
        _dialog = new("eplan_dialog", null) { Answers = _ => _approved ? ApprovalDecision.Granted : ApprovalDecision.Denied };
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.ActionRunPlan] = value =>
        {
            var args = (ActionRunArgs)value!;
            WritePlan plan = _harness.Plan([new() { Action = "run", Kind = "action", Name = args.Entry, NewValue = JsonSerializer.Serialize(Data(args, after: false).Parameters, HostJson.Options) }]);
            if (ActionRunCatalog.Text(ActionRunCatalog.Entries[args.Entry], "scope") == "system") { plan.Project = ""; plan.ProjectPath = ""; }
            plan.Details = new() { ["actionRun"] = JsonSerializer.Serialize(Data(args, after: false), HostJson.Options), ["effects"] = "Undo after commit unverified." };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ActionRunExecute] = value => Data((ActionRunArgs)value!, after: true);
        _harness.Bridge.Handlers[OperationNames.ActionRun] = value =>
        {
            var args = (ActionRunArgs)value!;
            var rule = ActionRunCatalog.Entries[args.Entry];
            string fileName = args.Parameters.Single(p => p.Name == rule["output"]!.GetValue<string>()).Value;
            if (args.Entry == "backup/MASTERDATA") { fileName += ActionRunCatalog.Extension(rule, args); }
            File.WriteAllBytes(Path.Combine(args.Directory!, _mode == "extension" ? "wrong.txt" : fileName), _mode == "empty" ? [] : args.Entry == "backup/MASTERDATA" && _mode != "header" ? [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c, 1] : [1, 2, 3]);
            if (_mode == "extra") { File.WriteAllText(Path.Combine(args.Directory!, "extra.txt"), "extra"); }
            if (_mode is "failed" or "timeout") { throw new BridgeCallException(new BridgeError { Reason = _mode == "timeout" ? ErrorReason.Timeout : ErrorReason.EplanOperationFailed, Message = "Action failed." }); }
            if (_mode == "cancel") { throw new OperationCanceledException(); }
            return Data(args, after: true);
        };
    }
    public void Dispose() => _harness.Dispose();
    private ActionRunTools Tool(ActionRunSwitch enabled = ActionRunSwitch.On) => new(_harness.Bridge, _harness.Pipeline, new ExportFolders(_harness.Locations, TimeProvider.System), _harness.Locations, _catalog,
        HostConfig.Defaults(_harness.Locations) with { ActionRun = enabled }, new AuditLog(_harness.Locations.Audit, TimeProvider.System), TimeProvider.System, NullLoggerFactory.Instance);
    private Task<CallToolResult> Run(string entry = "graphicallayertable/EXPORT", string arguments = "{\"EXPORTFILE\":\"layers\"}", string? project = "Line1", bool dry = false) => Tool().RunAsync(_progress, entry, Json(arguments), project, dry, TestContext.Current.CancellationToken);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public async Task Export_UsesExistingFolderFlow_HashesOneFile_AndNeverAsksApproval()
    {
        JsonElement data = WriteToolHarness.Structured(await Run());
        string directory = data.GetProperty("directory").GetString()!;
        Assert.StartsWith(Path.Combine(_harness.Locations.Exports, "Line1") + Path.DirectorySeparatorChar, directory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("layers.elc", data.GetProperty("files")[0].GetProperty("path").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3 })), data.GetProperty("files")[0].GetProperty("sha256").GetString());
        Assert.Equal((1, 3L), (data.GetProperty("fileCount").GetInt32(), data.GetProperty("totalBytes").GetInt64()));
        Assert.False(data.TryGetProperty("execution", out _));
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(_dialog.Questions);
        Assert.Equal(TimeSpan.FromSeconds(30), _harness.Bridge.Timeouts.Single());
        Assert.Single(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl"));
    }

    [Theory]
    [InlineData("ARTICLES", ".zw6", "parts-database")]
    [InlineData("LANGUAGES", ".zw7", "translation-database")]
    public async Task Backup_UsesTheDatabaseFolderAndArchiveHeader(string type, string extension, string folder)
    {
        JsonElement data = WriteToolHarness.Structured(await Run("backup/MASTERDATA", "{\"MDTYPE\":\"" + type + "\",\"ARCHIVENAME\":\"Library\"}", null));
        Assert.StartsWith(Path.Combine(_harness.Locations.Exports, folder), data.GetProperty("directory").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Library" + extension, data.GetProperty("files")[0].GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("project").ValueKind);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("extension")]
    [InlineData("extra")]
    [InlineData("failed")]
    [InlineData("timeout")]
    public async Task FailedExport_DeletesOnlyItsOwnFolder(string mode)
    {
        string parent = Path.Combine(_harness.Locations.Exports, "Line1");
        Directory.CreateDirectory(parent);
        string sentinel = Path.Combine(parent, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        _mode = mode;
        JsonElement error = WriteToolHarness.Error(await Run());
        Assert.Equal(mode == "timeout" ? ErrorReason.Timeout : ErrorReason.EplanOperationFailed, error.GetProperty("reason").GetString());
        Assert.Empty(Directory.GetDirectories(parent));
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public async Task CancelledExport_RemovesItsFolder()
    {
        _mode = "cancel";
        await Assert.ThrowsAsync<OperationCanceledException>(() => Run());
        Assert.Empty(Directory.GetDirectories(Path.Combine(_harness.Locations.Exports, "Line1")));
        string record = File.ReadAllText(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl").Single());
        Assert.Contains(ErrorReason.Cancelled, record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadArchiveHeader_IsRefusedAndRemoved()
    {
        _mode = "header";
        Assert.Equal(ErrorReason.EplanOperationFailed, WriteToolHarness.Error(await Run("backup/MASTERDATA", "{\"MDTYPE\":\"ARTICLES\",\"ARCHIVENAME\":\"Library\"}", null)).GetProperty("reason").GetString());
        Assert.Empty(Directory.GetDirectories(Path.Combine(_harness.Locations.Exports, "parts-database")));
    }

    [Fact]
    public async Task WriteAuditUnavailable_ExecutesNothing()
    {
        File.WriteAllText(_harness.Locations.Audit, "not a directory");
        JsonElement error = WriteToolHarness.Error(await Layer());
        Assert.Equal(ErrorReason.AuditUnavailable, error.GetProperty("reason").GetString());
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ExportAuditUnavailable_ReturnsErrorAndRemovesTheExport()
    {
        File.WriteAllText(_harness.Locations.Audit, "not a directory");
        Assert.Equal(ErrorReason.AuditUnavailable, WriteToolHarness.Error(await Run()).GetProperty("reason").GetString());
        Assert.Empty(Directory.GetDirectories(Path.Combine(_harness.Locations.Exports, "Line1")));
    }

    [Theory]
    [InlineData("EXPORTPARTS")]
    [InlineData("EXPORTMANUFACTURERS")]
    [InlineData("EXPORTCONSTRUCTIONS")]
    [InlineData("EXPORTCONNECTIONPATTERNS")]
    [InlineData("EXPORTACCESSORYLISTS")]
    [InlineData("EXPORTACCESSORYPLACEMENTS")]
    public async Task MissingFilter_IsRefusedByHostBeforeAnyBridgeCall(string type)
    {
        Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(await Run("partsmanagementapi/" + type, "{\"EXPORTFILE\":\"parts.xml\"}", null)).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
        Assert.False(Directory.Exists(_harness.Locations.Exports));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DryRun_CreatesNoFolderAndCallsOnlyPlan(bool write)
    {
        JsonElement data = WriteToolHarness.Structured(write ? await Run("changelayer", "{\"LAYER\":\"EPLAN100\",\"VISIBLE\":\"0\"}", dry: true) : await Run(dry: true));
        Assert.Equal("not_executed", data.GetProperty("readback").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("after").ValueKind);
        Assert.Equal(0, data.GetProperty("fileCount").GetInt32());
        Assert.False(Directory.Exists(_harness.Locations.Exports));
        Assert.Equal(OperationNames.ActionRunPlan, _harness.Bridge.Calls.Single().Operation);
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task WriteScratch_UsesWritePipelineWithoutBackup_AndReturnsTheObservedState()
    {
        JsonElement data = WriteToolHarness.Structured(await Layer());
        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("undo").ValueKind);
        Assert.Equal("fingerprint", data.GetProperty("readback").GetString());
        Assert.Single(_harness.Bridge.Writes);
        Assert.Empty(_dialog.Questions);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("directory").ValueKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealProject_RequiresExactlyOneApproval(bool approved)
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "real", "Line1");
        _approved = approved;
        CallToolResult result = await Layer();
        Assert.Single(_dialog.Questions);
        Assert.Equal(ToolTier.Write, _dialog.Questions.Single().Tier);
        Assert.Equal(approved ? 1 : 0, _harness.Bridge.Writes.Count);
        if (approved) { ToolResultAssert.Succeeded(result); }
        else { Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(result).GetProperty("reason").GetString()); }
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)]
    public async Task WriteErrors_KeepPipelineExecutionStatesAndNeverRepeat(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new() { Reason = reason, Execution = execution, Message = "failure" };
        JsonElement error = WriteToolHarness.Error(await Layer());
        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Single(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task RecoveredCommit_DoesNotInventAfterFingerprint()
    {
        _harness.Bridge.WriteRecovered = true;
        JsonElement data = WriteToolHarness.Structured(await Layer());
        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("after").ValueKind);
        Assert.Contains("not inferred", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadChangeBeyondDisplayedFiles_IsReportedAndDisablesTheEntry()
    {
        _manyFiles = true; _changed = true;
        JsonElement data = WriteToolHarness.Structured(await Run());
        Assert.True(data.GetProperty("entryDisabled").GetBoolean());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Equal(20, data.GetProperty("after").GetProperty("dataFiles").GetArrayLength());
        Assert.Equal(31, data.GetProperty("after").GetProperty("fileCount").GetInt32());
        int called = _harness.Bridge.Invocations;
        Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Run()).GetProperty("reason").GetString());
        Assert.Equal(called, _harness.Bridge.Invocations);
    }

    [Fact]
    public async Task OffAndVersionMismatch_ExecuteNothing()
    {
        Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Tool(ActionRunSwitch.Off).RunAsync(_progress, "graphicallayertable/EXPORT", Json("{\"EXPORTFILE\":\"layers\"}"), "Line1", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        _harness.Bridge.Status.Handshake!.Eplan.Version = "2025.0.3";
        Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Run()).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }
    private Task<CallToolResult> Layer() => Run("changelayer", "{\"LAYER\":\"EPLAN100\",\"VISIBLE\":\"0\"}");
    private ActionRunData Data(ActionRunArgs args, bool after)
    {
        var state = new Dictionary<string, string> { ["count:functions"] = "2876", ["file:Layer.eod"] = "100\t2026-10-22T00:00:00Z" };
        if (_manyFiles) { for (int i = 1; i <= 30; i++) { state["file:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = "100\t2026-10-22T00:00:00Z"; } }
        var last = new Dictionary<string, string>(state);
        if (_changed) { last["file:Layer.eod"] = "101\t2026-10-22T00:00:01Z"; }
        if (args.Entry == "changelayer") { state["layer:visible"] = "1"; last["layer:visible"] = "0"; }
        return new ActionRunData { Target = _harness.ProjectPath, Parameters = args.Parameters, Before = state, After = after ? last : null, Warning = "Undo after commit unverified." };
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;

namespace EplanMcp.Large.Tests;

/// <summary>Project list imports on LargeWrite only (S34/A39, tool spec 7). Never imports into the system parts database.</summary>
[Trait("Size", "Large")]
public sealed class ImportListTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_import_list";
    private readonly DestructiveHost _host;

    public ImportListTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Theory(Skip = "needs a device list sample from S34's export")]
    [InlineData("xml", "XDLXmlExporter")]
    [InlineData("csv", "XDLCsvImporterExporter")]
    public async Task Devices_PreparedS34Sample_ImportsTwoEntries(string format, string converter)
    {
        // README records the sizes and counts, not the XML/CSV schema. Supply S34's actual two-entry exports here.
        string sample = Path.Combine(AppContext.BaseDirectory, "fixtures", "s34-devices." + format);
        string file = ImportCopy(sample, "devices." + format);
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["kind"] = "devices", ["file"] = file, ["format"] = format };
        JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        Assert.Equal(0, dryRun.GetProperty("before").GetProperty("deviceListEntries").GetInt32());
        Assert.Equal(converter, dryRun.GetProperty("converter").GetString());
        Approve(dryRun);
        JsonElement result = await _host.CallAsync(Tool, args);
        Assert.Equal(2, result.GetProperty("after").GetProperty("deviceListEntries").GetInt32());
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("undo").ValueKind);
        Assert.True(File.Exists(result.GetProperty("backup").GetString()));
    }

    [Fact]
    public async Task Parts_ExportThenImport_AddsS34sTwoReferences_WithBackupAndProgressWithin300Seconds()
    {
        JsonElement exported = await _host.CallAsync("eplan_export_parts", new() { ["project"] = _host.Writable, ["file_name"] = "S34Parts" });
        string source = Path.Combine(exported.GetProperty("directory").GetString()!, Assert.Single(exported.GetProperty("files").EnumerateArray()).GetProperty("path").GetString()!);
        string file = ImportCopy(source, "parts.xml"); // Exports have their own fixed root; stage the file under imports.
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["kind"] = "parts", ["file"] = file };
        JsonElement projectBefore = await _host.CallAsync("eplan_project_get", new() { ["project"] = _host.Writable });
        JsonElement functionsBefore = await _host.CallAsync("eplan_device_list", new() { ["project"] = _host.Writable, ["include_auxiliary"] = true, ["limit"] = 1 });
        int asked = _host.Approvals.Questions.Count;
        string[] backups = _host.BackupFolders();
        JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.Null),
            (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("after").ValueKind, dryRun.GetProperty("backup").ValueKind));
        Assert.Equal(asked, _host.Approvals.Questions.Count);
        Assert.Equal(backups, _host.BackupFolders());
        int references = dryRun.GetProperty("before").GetProperty("articleReferences").GetInt32();
        TestContext.Current.TestOutputHelper?.WriteLine($"Baseline: {references} article references (S34: 2900); expected delta +2.");

        JsonElement denied = await _host.ErrorAsync(Tool, args);
        Assert.Equal(("APPROVAL_DENIED", "not_started"), (denied.GetProperty("reason").GetString(), denied.GetProperty("execution").GetString()));
        Assert.Equal(backups, _host.BackupFolders());

        Approve(dryRun);
        var progress = new ImportProgress();
        var elapsed = Stopwatch.StartNew();
        JsonElement imported = await _host.CallAsync(Tool, args, progress);
        elapsed.Stop();
        Assert.InRange(elapsed.Elapsed.TotalSeconds, 0, 300);
        Assert.Equal("committed", imported.GetProperty("execution").GetString());
        Assert.Equal("XPalXmlExporter", imported.GetProperty("converter").GetString());
        Assert.Equal(references + 2, imported.GetProperty("after").GetProperty("articleReferences").GetInt32());
        Assert.Equal(imported.GetProperty("before").GetProperty("projectParts").GetInt32(), imported.GetProperty("after").GetProperty("projectParts").GetInt32());
        Assert.Equal(JsonValueKind.Null, imported.GetProperty("undo").ValueKind);
        Assert.True(File.Exists(imported.GetProperty("backup").GetString()));
        JsonElement projectAfter = await _host.CallAsync("eplan_project_get", new() { ["project"] = _host.Writable });
        JsonElement functionsAfter = await _host.CallAsync("eplan_device_list", new() { ["project"] = _host.Writable, ["include_auxiliary"] = true, ["limit"] = 1 });
        Assert.Equal(functionsBefore.GetProperty("total").GetInt32(), functionsAfter.GetProperty("total").GetInt32());
        foreach (string field in new[] { "pages", "devices", "connections" })
        {
            Assert.Equal(projectBefore.GetProperty("counts").GetProperty(field).GetInt32(), projectAfter.GetProperty("counts").GetProperty(field).GetInt32());
        }

        (TimeSpan At, string Message)[] heartbeats = [.. progress.Reports.Where(p => p.Message.StartsWith("Importing project list (", StringComparison.Ordinal))];
        Assert.True(heartbeats.Length >= 2, "Expected repeated heartbeat progress during the minute-long parts import.");
        foreach (var pair in heartbeats.Zip(heartbeats.Skip(1)))
        {
            Assert.InRange((pair.Second.At - pair.First.At).TotalSeconds, 3, 8);
        }

        Assert.Contains(_host.AuditRecords(), r => r["tool"]?.GetValue<string>() == Tool && r["execution"]?.GetValue<string>() == "committed");
    }

    private string ImportCopy(string source, string name)
    {
        string relative = "s34/" + Guid.NewGuid().ToString("N") + "-" + name;
        string target = Path.Combine(_host.Home, "imports", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
        return relative;
    }

    private void Approve(JsonElement plan) => _host.Approvals.Approve(Tool, _host.Writable,
        [$"import {plan.GetProperty("kind").GetString()} list into project {plan.GetProperty("project").GetString()} from {plan.GetProperty("file").GetString()} ({plan.GetProperty("bytes").GetInt64().ToString(CultureInfo.InvariantCulture)} bytes, SHA-256 {plan.GetProperty("sha256").GetString()}); converter {plan.GetProperty("converter").GetString()}; before {plan.GetProperty("before").GetRawText()}"]);

    private sealed class ImportProgress : IProgress<ProgressNotificationValue>
    {
        private readonly Lock _lock = new();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly List<(TimeSpan At, string Message)> _reports = [];
        public (TimeSpan At, string Message)[] Reports { get { lock (_lock) { return [.. _reports]; } } }
        public void Report(ProgressNotificationValue value) { lock (_lock) { _reports.Add((_elapsed.Elapsed, value.Message ?? string.Empty)); } }
    }
}

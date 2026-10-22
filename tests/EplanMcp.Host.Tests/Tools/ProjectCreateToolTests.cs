using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>Normal pipeline and real Bridge.Core operations, with fake EPLAN adapters; no EPLAN or pipes.</summary>
[Trait("Size", "Medium")]
public sealed class ProjectCreateToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);
    private readonly ProjectCreateBridge _adapter = new();
    private readonly List<IDisposable> _locks = [];
    private string Target => Path.Combine(_harness.Locations.DefaultScratch, "projects", "Line1");

    public ProjectCreateToolTests() => Configure(_harness, _adapter);
    public void Dispose()
    {
        foreach (IDisposable item in _locks)
        { item.Dispose(); }
        _harness.Dispose();
    }

    [Theory]
    [InlineData("basic_project", 120, 1)]
    [InlineData("backup", 180, null)]
    public async Task BothModes_PlanAnAbsentTarget_AndExecuteCreatesAndChecksIt(string mode, int limit, int? pages)
    {
        byte[] before = System.IO.File.ReadAllBytes(Source(_harness, mode));
        CallToolResult result = await Create(mode);
        JsonElement data = WriteToolHarness.Structured(result);
        string link = Path.Combine(Target, "Line1.elk");
        Assert.Equal(("committed", mode, "18.0", "Line1", link, false),
            (data.GetProperty("execution").GetString(), data.GetProperty("mode").GetString(), data.GetProperty("databaseVersion").GetString(),
            data.GetProperty("project").GetProperty("name").GetString(), data.GetProperty("project").GetProperty("path").GetString(), data.GetProperty("project").GetProperty("opened").GetBoolean()));
        Assert.Equal(pages, data.GetProperty("pages").Deserialize<int?>());
        Assert.Equal(mode == "backup" ? null : "basic@1", data.GetProperty("projectGuid").GetString());
        Assert.InRange(data.GetProperty("seconds").GetDouble(), 0, 30);
        Assert.Equal("The new project is closed.", data.GetProperty("hint").GetString());
        Assert.Equal(mode == "backup" ? "Restored backup as new project 'Line1'; it is closed." : "Created project 'Line1' from a basic project; it is closed.", WriteToolHarness.Text(result, 0));
        Assert.Equal(data.GetRawText(), WriteToolHarness.Text(result, 1));
        Assert.Equal([Plan(mode), Execute(mode)], _harness.Bridge.Calls.Select(c => c.Operation));
        Assert.Equal(["make:" + link, "compatibility:" + link], _adapter.Events);
        Assert.Equal(TimeSpan.FromSeconds(limit), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(Source(_harness, mode)));
        JsonElement[] audit = Audit(_harness);
        Assert.Equal(["execution_started", "execution_result"], audit.Select(r => r.GetProperty("kind").GetString()));
        Assert.Equal("committed", audit[^1].GetProperty("execution").GetString());
        AssertError(await Create(mode), ErrorReason.AlreadyExists, ExecutionState.NotStarted);
    }

    [Theory]
    [InlineData("basic_project")]
    [InlineData("backup")]
    public async Task DryRun_OnlyPlans_AndCreatesNoProjectOrParentDirectories(string mode)
    {
        JsonElement data = WriteToolHarness.Structured(await Create(mode, dryRun: true));
        Assert.Equal("not_started", data.GetProperty("execution").GetString());
        Assert.False(Directory.Exists(_harness.Locations.DefaultScratch));
        Assert.Equal(Plan(mode), Assert.Single(_harness.Bridge.Calls).Operation);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(_adapter.Events);
        foreach (string p in new[] { "pages", "projectGuid", "databaseVersion" })
        { Assert.Equal(JsonValueKind.Null, data.GetProperty(p).ValueKind); }
        JsonElement change = Assert.Single(data.GetProperty("changes").EnumerateArray());
        Assert.Equal(("create", "project", "Line1"), (change.GetProperty("action").GetString(), change.GetProperty("kind").GetString(), change.GetProperty("name").GetString()));
        Assert.Contains(data.GetProperty("sha256").GetString()!, change.GetProperty("newValue").GetString(), StringComparison.Ordinal);
        Assert.Contains(Target, change.GetProperty("newValue").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingEmptyFolder_OrFile_IsRefusedBeforePlanningAndUnchanged(bool file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        if (file)
        { System.IO.File.WriteAllText(Target, "engineer's work"); }
        else
        { Directory.CreateDirectory(Target); }
        AssertError(await Create(), ErrorReason.AlreadyExists, ExecutionState.NotStarted);
        Assert.Empty(_harness.Bridge.Calls);
        if (file)
        { Assert.Equal("engineer's work", System.IO.File.ReadAllText(Target)); }
        else
        { Assert.Empty(Directory.EnumerateFileSystemEntries(Target)); }
    }

    [Theory]
    [InlineData("basic_project")]
    [InlineData("backup")]
    public async Task IncompatibleMaterial_IsCleanedByBridge_AndThePipelineAuditsRollback(string mode)
    {
        _adapter.Status = "IsNewer";
        AssertError(await Create(mode), ErrorReason.InvalidArgument, ExecutionState.RolledBack);
        Assert.False(Directory.Exists(Target));
        Assert.Equal("rolled_back", Audit(_harness)[^1].GetProperty("execution").GetString());
        Assert.Equal(ErrorReason.InvalidArgument, Audit(_harness)[^1].GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("basic_project")]
    [InlineData("backup")]
    public async Task CompatibilityFailure_UsesTheExistingBridgeCleanupAndAudit(string mode)
    {
        _adapter.CheckFailure = new InvalidOperationException("failed");
        AssertError(await Create(mode), ErrorReason.EplanOperationFailed, ExecutionState.RolledBack);
        Assert.False(Directory.Exists(Target));
        Assert.Equal("rolled_back", Audit(_harness)[^1].GetProperty("execution").GetString());
    }

    [Fact]
    public async Task CleanupFailure_IsUnknown_AndTheNormalPipelineSuspendsWrites()
    {
        _adapter.Status = "IsNewer";
        _adapter.BeforeCheck = link => _locks.Add(System.IO.File.Open(link, FileMode.Open, FileAccess.Read, FileShare.None));
        AssertError(await Create(), ErrorReason.OutcomeUnknown, ExecutionState.Unknown);
        Assert.True(Directory.Exists(Target));
        Assert.Contains(Audit(_harness), r => r.GetProperty("kind").GetString() == "writes_suspended");
        Assert.NotEmpty(SuspensionStore.For(_harness.Locations).Blocking(Path.Combine(Target, "Line1.elk"), Environment.ProcessId, SuspensionStore.StartOf(Environment.ProcessId)!.Value, null));
    }

    [Fact]
    public async Task BridgeCleanup_DeletesLinksWithoutFollowingThem()
    {
        string elsewhere = Directory.CreateDirectory(Path.Combine(_harness.Locations.Root, "elsewhere")).FullName;
        string sentinel = Path.Combine(elsewhere, "keep.txt");
        System.IO.File.WriteAllText(sentinel, "keep");
        _adapter.Status = "IsNewer";
        _adapter.BeforeCheck = link => Junction(Path.Combine(Path.ChangeExtension(link, ".edb"), "link"), elsewhere);
        AssertError(await Create(), ErrorReason.InvalidArgument, ExecutionState.RolledBack);
        Assert.Equal("keep", System.IO.File.ReadAllText(sentinel));
        Assert.False(Directory.Exists(Target));
    }

    [Theory]
    [InlineData("basic_project")]
    [InlineData("backup")]
    public async Task MissingProjectFiles_AreRolledBackBeforeCompatibility(string mode)
    {
        _adapter.MakeFiles = false;
        AssertError(await Create(mode), ErrorReason.EplanOperationFailed, ExecutionState.RolledBack);
        Assert.False(Directory.Exists(Target));
        Assert.Single(_adapter.Events);
    }

    [Theory]
    [InlineData((int)ApprovalDecision.Granted, null)]
    [InlineData((int)ApprovalDecision.Denied, ErrorReason.ApprovalDenied)]
    [InlineData((int)ApprovalDecision.Expired, ErrorReason.ApprovalExpired)]
    public async Task OutsideScratch_AsksEveryTime_WhileTheDestinationRemainsAbsent(int decision, string? reason)
    {
        string? outside = null;
        var channel = new FakeApprovalChannel("eplan_dialog", (ApprovalDecision)decision) { Wait = () => Assert.False(Directory.Exists(outside)) };
        using var harness = new WriteToolHarness(channel);
        Configure(harness, new());
        outside = Path.Combine(harness.Locations.Root, "outside");
        CallToolResult result = await Call(harness, destination: outside, cancellationToken: TestContext.Current.CancellationToken);
        ApprovalQuestion question = Assert.Single(channel.Questions);
        Assert.Contains("sha256", question.Changes[0], StringComparison.Ordinal);
        Assert.Contains(outside, question.Changes[0], StringComparison.Ordinal);
        Assert.Contains("no EPLAN undo step", ApprovalTexts.For(question, CultureInfo.InvariantCulture).Note, StringComparison.Ordinal);
        if (reason is not null)
        {
            AssertError(result, reason, ExecutionState.NotStarted);
            Assert.False(Directory.Exists(outside));
            Assert.Empty(harness.Bridge.Writes);
        }
        else
        {
            WriteToolHarness.Structured(result);
            Assert.Equal(question.Plan.Fingerprint, ((NewProjectArgs)harness.Bridge.Calls.Single(c => c.Operation == Execute("basic_project")).Args!).Fingerprint);
            outside = Path.Combine(harness.Locations.Root, "outside2");
            WriteToolHarness.Structured(await Call(harness, destination: outside, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(2, channel.Questions.Count);
        }
    }

    [Fact]
    public async Task PolicyDenial_AndAuditFailure_CreateNoDirectories()
    {
        AssertError(await Create(destination: Path.Combine(_harness.Locations.Root, "outside")), ErrorReason.PolicyDenied, ExecutionState.NotStarted);
        System.IO.File.WriteAllText(_harness.Locations.Audit, "not a directory");
        AssertError(await Create(), ErrorReason.AuditUnavailable, ExecutionState.NotStarted);
        Assert.False(Directory.Exists(_harness.Locations.DefaultScratch));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedOrRemovedSourceAfterApproval_IsStale_EvenWithSameSizeAndTime(bool remove)
    {
        WriteToolHarness? active = null;
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                string file = Source(active!, "basic_project");
                DateTime written = System.IO.File.GetLastWriteTimeUtc(file);
                if (remove)
                { System.IO.File.Delete(file); }
                else
                { System.IO.File.WriteAllBytes(file, [9, 8, 7]); System.IO.File.SetLastWriteTimeUtc(file, written); }
            },
        };
        using var harness = new WriteToolHarness(channel);
        active = harness;
        Configure(harness, new());
        AssertError(await Call(harness, destination: Path.Combine(harness.Locations.Root, "outside"), cancellationToken: TestContext.Current.CancellationToken), ErrorReason.PlanStale, ExecutionState.NotStarted);
        Assert.Empty(harness.Bridge.Writes);
        Assert.False(Directory.Exists(Path.Combine(harness.Locations.Root, "outside")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DestinationAppearingAfterThePlan_IsNeverReused(bool withFile)
    {
        string? target = null;
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                Directory.CreateDirectory(target!);
                if (withFile)
                { System.IO.File.WriteAllText(Path.Combine(target!, "work.txt"), "engineer's work"); }
            },
        };
        using var harness = new WriteToolHarness(channel);
        Configure(harness, new());
        target = Path.Combine(harness.Locations.Root, "outside");
        AssertError(await Call(harness, destination: target, cancellationToken: TestContext.Current.CancellationToken), ErrorReason.AlreadyExists, ExecutionState.NotStarted);
        if (withFile)
        { Assert.Equal("engineer's work", System.IO.File.ReadAllText(Path.Combine(target, "work.txt"))); }
        else
        { Assert.Empty(Directory.EnumerateFileSystemEntries(target)); }
        Assert.Empty(harness.Bridge.Writes);
    }

    [Fact]
    public async Task ATargetAppearingAtBridgeExecution_IsRefusedWithoutChangingItsBytes()
    {
        _harness.Bridge.DuringWrite = _ => System.IO.File.WriteAllText(Path.Combine(Directory.CreateDirectory(Target).FullName, "Line1.elk"), "engineer's work");
        AssertError(await Create(), ErrorReason.AlreadyExists, ExecutionState.NotStarted);
        Assert.Equal("engineer's work", System.IO.File.ReadAllText(Path.Combine(Target, "Line1.elk")));
        Assert.Empty(_adapter.Events);
    }

    [Fact]
    public async Task UnknownExecution_IsNeitherCleanedNorRepeated()
    {
        _harness.Bridge.DuringWrite = _ => System.IO.File.WriteAllText(Path.Combine(Directory.CreateDirectory(Target).FullName, "Line1.elk"), "still writing");
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Message = "unknown", Execution = ExecutionState.Unknown };
        AssertError(await Create(), ErrorReason.OutcomeUnknown, ExecutionState.Unknown);
        Assert.Equal("still writing", System.IO.File.ReadAllText(Path.Combine(Target, "Line1.elk")));
        Assert.Single(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task CancellationAfterDelivery_IsAuditedThroughTheNormalPipeline()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _harness.Bridge.DuringWrite = _ => cancel.Cancel();
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.Cancelled, Message = "cancelled", Execution = ExecutionState.RolledBack };
        AssertError(await Call(_harness, cancellationToken: cancel.Token), ErrorReason.Cancelled, ExecutionState.RolledBack);
        Assert.False(Directory.Exists(Target));
        Assert.Equal("CANCELLED", Audit(_harness)[^1].GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("basic_project")]
    [InlineData("backup")]
    public async Task Progress_UsesTheExistingPipelineWithoutToolSpecificHooks(string mode)
    {
        var progress = new RecordedProgress();
        _harness.Bridge.DuringWrite = reports =>
        {
            reports!.Report(new ProgressNotification());
            reports.Report(new ProgressNotification { Message = "Finishing the project" });
        };
        WriteToolHarness.Structured(await ProjectCreateTools.CreateAsync(_harness.Pipeline, new(_harness.Locations), Config(_harness),
            mode, "material" + (mode == "backup" ? ".zw1" : ".zw9"), "Line1", progress: progress, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(mode == "backup" ? "Restoring the backup" : "Creating the project", progress.Values[0].Message);
        Assert.Equal(progress.Values.Select(v => v.Progress).Order(), progress.Values.Select(v => v.Progress));
        Assert.All(progress.Values, v => Assert.Null(v.Total));
    }

    [Fact]
    public async Task ExplicitScratchDestination_IsNormalizedAndAutomatic()
    {
        string parent = Directory.CreateDirectory(Path.Combine(_harness.Locations.DefaultScratch, "explicit")).FullName;
        JsonElement data = WriteToolHarness.Structured(await Create(destination: Path.Combine(parent, "Line1").Replace('\\', '/')));
        Assert.Equal(Path.Combine(parent, "Line1", "Line1.elk"), data.GetProperty("project").GetProperty("path").GetString());
        Assert.DoesNotContain(Audit(_harness), r => r.GetProperty("kind").GetString() == "approval");
    }

    [Fact]
    public async Task MissingScratchConfiguration_OrExplicitParent_IsRejected()
    {
        AssertError(await ProjectCreateTools.CreateAsync(_harness.Pipeline, new(_harness.Locations), new([], OutsideScratchWrites.Deny),
            "basic_project", "material.zw9", "Line1", cancellationToken: TestContext.Current.CancellationToken), ErrorReason.PolicyDenied, ExecutionState.NotStarted);
        AssertError(await Create(destination: Path.Combine(_harness.Locations.Root, "absent", "Line1")), ErrorReason.InvalidArgument, ExecutionState.NotStarted);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task TheConfiguredScratchRootMayBeAJunction()
    {
        string physical = Directory.CreateDirectory(Path.Combine(_harness.Locations.Root, "scratch-real")).FullName;
        Junction(_harness.Locations.DefaultScratch, physical);
        try
        {
            WriteToolHarness.Structured(await Create());
            Assert.True(System.IO.File.Exists(Path.Combine(physical, "projects", "Line1", "Line1.elk")));
        }
        finally { Directory.Delete(_harness.Locations.DefaultScratch); }
    }

    [Theory]
    [InlineData("target")]
    [InlineData("parent")]
    [InlineData("source")]
    public async Task LowerJunctionsAreRejected_WithoutWritingToTheirDestination(string where)
    {
        string elsewhere = Directory.CreateDirectory(Path.Combine(_harness.Locations.Root, "elsewhere")).FullName;
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        string link = where == "target" ? Target : where == "parent" ? Path.GetDirectoryName(Target)! : Path.Combine(_harness.Locations.Imports, "linked");
        if (where == "parent")
        { Directory.Delete(link); }
        Junction(link, elsewhere);
        try
        {
            CallToolResult result = where == "source"
                ? await ProjectCreateTools.CreateAsync(_harness.Pipeline, new(_harness.Locations), Config(_harness), "basic_project", "linked/material.zw9", "Line1", cancellationToken: TestContext.Current.CancellationToken)
                : await Create();
            AssertError(result, where == "target" ? ErrorReason.AlreadyExists : ErrorReason.InvalidArgument, ExecutionState.NotStarted);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task OversizedOrMissingMaterials_NeverReachTheBridge()
    {
        using (var stream = System.IO.File.OpenWrite(Source(_harness, "basic_project")))
        { stream.SetLength(ImportTools.MaxProjectBytes + 1); }
        AssertError(await Create(), ErrorReason.InvalidArgument, ExecutionState.NotStarted);
        System.IO.File.Delete(Source(_harness, "basic_project"));
        JsonElement error = AssertError(await Create(), ErrorReason.FileNotFound, ExecutionState.NotStarted);
        Assert.Equal("material.zw9", error.GetProperty("metadata").GetProperty("file").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Create(string mode = "basic_project", bool dryRun = false, string? destination = null) => Call(_harness, mode, dryRun, destination, TestContext.Current.CancellationToken);
    private static Task<CallToolResult> Call(WriteToolHarness harness, string mode = "basic_project", bool dryRun = false, string? destination = null, CancellationToken cancellationToken = default) =>
        ProjectCreateTools.CreateAsync(harness.Pipeline, new(harness.Locations), Config(harness), mode, "material" + (mode == "backup" ? ".zw1" : ".zw9"), "Line1", destination, dryRun, cancellationToken: cancellationToken == default ? TestContext.Current.CancellationToken : cancellationToken);
    private static HostConfig Config(WriteToolHarness harness) => new([harness.Locations.DefaultScratch], OutsideScratchWrites.Deny);
    private static string Source(WriteToolHarness harness, string mode) => Path.Combine(harness.Locations.Imports, "material" + (mode == "backup" ? ".zw1" : ".zw9"));
    private static string Plan(string mode) => mode == "backup" ? OperationNames.ProjectRestorePlan : OperationNames.ProjectCreatePlan;
    private static string Execute(string mode) => mode == "backup" ? OperationNames.ProjectRestoreExecute : OperationNames.ProjectCreateExecute;
    private static JsonElement[] Audit(WriteToolHarness harness) => [.. Directory.EnumerateFiles(harness.Locations.Audit, "*.jsonl").SelectMany(System.IO.File.ReadLines).Select(s => JsonDocument.Parse(s).RootElement.Clone())];
    private static JsonElement AssertError(CallToolResult result, string reason, string execution)
    {
        JsonElement error = WriteToolHarness.Error(result);
        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(reason == ErrorReason.OutcomeUnknown, execution == ExecutionState.Unknown);
        if (error.GetProperty("retryable").GetBoolean())
        { Assert.Contains(execution, new[] { ExecutionState.NotStarted, ExecutionState.RolledBack }); }
        return error;
    }

    private static void Configure(WriteToolHarness harness, ProjectCreateBridge adapter)
    {
        Directory.CreateDirectory(harness.Locations.Imports);
        foreach (string mode in new[] { "basic_project", "backup" })
        {
            System.IO.File.WriteAllBytes(Source(harness, mode), [1, 2, 3]);
            harness.Bridge.Handlers[Plan(mode)] = args => ProjectCreateBridge.Plan((NewProjectArgs)args!, mode);
            harness.Bridge.Handlers[Execute(mode)] = args => adapter.Execute((NewProjectArgs)args!, mode);
        }
    }

    private static void Junction(string path, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>Opt-in serial trials on LargeWrite only; evidence and suspensions survive an interrupted run.</summary>
[Trait("Size", "Large"), Trait("Batch", "Verification")]
public sealed class BatchVerification(LiveEplan eplan)
{
    public static bool Enabled => Environment.GetEnvironmentVariable("EPLANMCP_BATCH_VERIFY") == "1";
    private readonly List<BatchObservation> _results = [];
    private readonly PropertySchemaPaging _paging = new(TimeProvider.System);
    private readonly IProgress<ProgressNotificationValue> _progress = new Progress<ProgressNotificationValue>();
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string? _stoppedAt, _stopReason;
    private string _version = "", _date = "", _file = "";
    private Dictionary<string, int> _expected = [];
    private int _messageBookmark;

    [Fact(SkipUnless = nameof(Enabled), Skip = "Set EPLANMCP_BATCH_VERIFY=1 to authorize batch trials on the prepared LargeWrite copy.")]
    public async Task VerifyCatalog()
    {
        double minutes = double.Parse(Environment.GetEnvironmentVariable("EPLANMCP_BATCH_MINUTES") ?? "120", CultureInfo.InvariantCulture);
        Assert.True(double.IsFinite(minutes) && minutes > 0 && minutes <= 1440, "Batch minutes must be between 0 and 1440.");
        string root = LiveEplan.ScratchRoot();
        Assert.False(InRepository(root), "Batch evidence must never be stored in the repository.");
        string home = Directory.CreateDirectory(Path.Combine(root, "batch-verification", Guid.NewGuid().ToString("N"))).FullName;
        _file = Path.Combine(home, "results.json");
        _date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        TestContext.Current.TestOutputHelper!.WriteLine("Batch results: " + _file);
        // Persist the path before touching EPLAN, even if setup subsequently fails.
        Save();
        var services = new ServiceCollection();
        services.AddLogging(l => l.ClearProviders());
        services.AddSingleton(new HostDataLocations(home));
        services.AddSingleton(new HostConfig([WriteHost.ScratchFolder], OutsideScratchWrites.Deny) { ApiTry = ActionRunSwitch.On, ActionTry = ActionRunSwitch.On });
        services.AddEplanMcp();
        services.RemoveAll<IApprovalChannel>();
        var approvals = new TrialApprovals(ApiTryTools.Tool, ActionTryTools.Tool);
        services.AddSingleton<IApprovalChannel>(approvals);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IBridgeClient bridge = provider.GetRequiredService<IBridgeClient>();
        _version = (await bridge.SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken)).Eplan.Version;
        BatchEntry[] entries = BatchValues.Entries(ActionCatalogReader.Official());
        _expected = BatchValues.ExpectedCounts(entries);
        foreach (var count in _expected)
        { TestContext.Current.TestOutputHelper.WriteLine($"Expected {count.Key}: {count.Value}"); }
        Save();
        entries = BatchValues.Resume(entries, Environment.GetEnvironmentVariable("EPLANMCP_BATCH_FROM"), Environment.GetEnvironmentVariable("EPLANMCP_BATCH_AFTER"));
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        budget.CancelAfter(TimeSpan.FromMinutes(minutes));
        var watch = Stopwatch.StartNew();
        await using PlanStepLiveSession alone = await PlanStepLiveSession.StartAsync(eplan);
        await alone.EnsureAloneAsync();
        Assert.Equal([alone.Project], await alone.OpenPathsAsync());
        Assert.True(ScratchPaths.IsInside(alone.Project, [WriteHost.ScratchFolder]));
        try
        {
            foreach (BatchEntry entry in entries)
            {
                if (budget.IsCancellationRequested)
                { _stoppedAt = entry.Id; _stopReason = "run_budget"; break; }
                var observation = new BatchObservation(entry.Id, entry.Kind);
#pragma warning disable CA2000 // The scoped using disposes the entry token on success, failure, and the stop-run break.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, TestContext.Current.CancellationToken);
#pragma warning restore CA2000
                // Writes snapshot the whole copy before and after, apply, read back and undo: 16-30 s on LargeWrite (first run).
                timeout.CancelAfter(TimeSpan.FromSeconds(entry.Kind == "api-read" ? 30 : 90));
                Stopwatch elapsed = Stopwatch.StartNew();
                _stoppedAt = entry.Id;
                _stopReason = "in_progress";
                Save();
                try
                {
                    await VerifyAsync(entry, observation, alone, provider, timeout.Token).WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                { observation.Outcome = "timeout"; observation.Detail = budget.IsCancellationRequested ? "run_budget" : "entry_budget"; }
                catch (BridgeCallException ex)
                { Error(observation, ex.Error); }
                catch (Exception ex)
                {
                    // Unexpected observation failures cannot establish the outcome of a trial already sent.
                    observation.Outcome = "outcome_unknown";
                    observation.Detail = ex.GetType().Name + ": " + ex.Message;
                }
                observation.Seconds = elapsed.Elapsed.TotalSeconds;
                bool lost = BatchValues.EplanLost(observation, ProcessAlive());
                _results.Add(observation);
                if (_results.Count % 25 == 0)
                { TestContext.Current.TestOutputHelper.WriteLine($"Batch progress: {_results.Count}; elapsed: {watch.Elapsed}; current: {entry.Id}"); }
                if (BatchValues.Stops(observation))
                {
                    _stopReason = lost ? "eplan_lost" : observation.Outcome;
                    alone.PauseCleanup();
                    // Evidence must survive even if recording the persistent suspension fails.
                    Save();
                    Suspend(alone.Project, provider, entry.Id);
                    break;
                }
                _stoppedAt = null;
                _stopReason = null;
                Save();
            }
        }
        finally
        {
            Save();
            TestContext.Current.TestOutputHelper.WriteLine($"Batch results: {_file}; elapsed: {watch.Elapsed.TotalMinutes:0.0} min; stopped: {_stoppedAt ?? "complete"} ({_stopReason ?? "none"})");
        }
    }

    private async Task VerifyAsync(BatchEntry entry, BatchObservation result, PlanStepLiveSession alone, ServiceProvider provider, CancellationToken cancellationToken)
    {
        if (BatchValues.SkipRefused(entry, result))
        { return; }
        IBridgeClient bridge = provider.GetRequiredService<IBridgeClient>();
        string project = alone.Project;
        if (SuspensionStore.For(HostDataLocations.FromEnvironment()).Blocking(project, eplan.ProcessId, ProcessStart()).Count > 0)
        {
            result.Outcome = "outcome_unknown";
            result.Detail = "Writes remain suspended; resume does not authorize releasing a previous unknown outcome.";
            return;
        }
        if (entry.Kind != "api-read" && entry.NeedsContext)
        { result.Outcome = "failed:needs_context"; return; }
        if (entry.Kind == "action")
        {
            string page = (await PagesAsync(bridge, project, cancellationToken)).Pages.OrderBy(p => p.Name, StringComparer.Ordinal).First().Name;
            // Same operation as eplan_page_open, with the entry's cancellation token (A61).
            await bridge.InvokeAsync<PageOpenArgs, PageOpenData>(OperationNames.PageOpen, new() { Project = project, Page = page }, cancellationToken);
        }
        BatchArguments arguments = entry.Kind == "api-read"
            ? await new BatchReadDiscovery(bridge, project).ArgumentsAsync(entry, cancellationToken)
            : new(await BatchValues.ArgumentsAsync(entry, kinds => LocatorAsync(bridge, project, kinds, cancellationToken)));
        Dictionary<string, object?>? parameters = arguments.Values;
        if (parameters is null)
        { result.Outcome = arguments.Outcome ?? "failed:needs_context"; result.Detail = arguments.Detail; return; }
        if (entry.Kind != "api-read")
        {
            JsonElement? target = entry.ObjectKinds is null ? null : await LocatorAsync(bridge, project, entry.ObjectKinds, cancellationToken);
            if (entry.ObjectKinds is not null && target is null)
            { result.Outcome = "skipped:no_object"; return; }
            if (target is not null)
            { parameters["target"] = target; }
        }
        string group = entry.Group;
        var api = ActivatorUtilities.CreateInstance<ApiTryTools>(provider);
        string? originalValue = null;
        bool propertyWrite = entry.Kind == "api-write" && !entry.Method;
        if (propertyWrite)
        {
            JsonElement read = Data(await api.TryAsync(_progress, group, project, JsonSerializer.SerializeToElement(parameters), dry_run: false, cancellationToken: cancellationToken));
            originalValue = read.GetProperty("value").GetString();
            object? next = BatchValues.Next(entry.ValueShape![0], originalValue, entry.ValueShape[1..]);
            if (next is null)
            { result.Outcome = "skipped:no_simple_value"; return; }
            parameters["value"] = next;
        }
        string before = (await PagesAsync(bridge, project, cancellationToken)).Hash;
        Dictionary<string, string> design = LiveSnapshots.DesignFiles(project);
        _messageBookmark = await MessagesAsync(bridge, cancellationToken);
        var approvals = (TrialApprovals)provider.GetRequiredService<IApprovalChannel>();
        int questionsBefore = approvals.Questions.Count;
        CallToolResult call = entry.Kind == "action"
            ? await ActivatorUtilities.CreateInstance<ActionTryTools>(provider).TryAsync(project, entry.Action!.Name, entry.Action.Type, JsonSerializer.SerializeToElement(parameters), dry_run: false, cancellationToken: cancellationToken)
            : await api.TryAsync(_progress, group, project, JsonSerializer.SerializeToElement(parameters), dry_run: false, cancellationToken: cancellationToken);
        if (entry.Destructive)
        {
            // Keep the production destructive pipeline, including backup and its bound per-call approval.
            ApprovalQuestion[] questions = [.. approvals.Questions.Skip(questionsBefore)];
            if (call.IsError != true)
            { Assert.NotEmpty(questions); }
            Assert.All(questions,
                question => { Assert.Equal(ToolTier.Destructive, question.Tier); Assert.NotNull(question.BackupFolder); });
        }
        JsonElement data = Payload(call);
        if (call.IsError == true)
        {
            Error(result, data.Deserialize<BridgeError>(HostJson.Options)!);
            if (BatchValues.EplanLost(result, processAlive: true) || result.Outcome is "outcome_unknown" or "modal" or "timeout")
            { return; }
        }
        else
        {
            result.Outcome = data.GetProperty("execution").GetString() switch
            { "committed" => "ok", "rolled_back" => "rolled_back", "unknown" => "outcome_unknown", _ => "failed" };
            if (data.TryGetProperty("recovered", out JsonElement recovered) && recovered.GetBoolean())
            { result.Outcome = "outcome_unknown"; }
            if (result.Outcome == "outcome_unknown")
            { return; }
        }
        result.EplanMessages = Math.Max(0, await MessagesAsync(bridge, cancellationToken) - _messageBookmark);
        BatchValues.Classify(result);
        result.Changed = before != (await PagesAsync(bridge, project, cancellationToken)).Hash || !SameDesign(design, LiveSnapshots.DesignFiles(project));
        if (result.Outcome != "ok" && result.Changed)
        { result.UndoRestores = false; return; }
        bool writeMatches = true;
        if (propertyWrite && result.Outcome == "ok")
        {
            var readParameters = new Dictionary<string, object?>(parameters);
            readParameters.Remove("value");
            readParameters["target"] = await LocatorAsync(bridge, project, entry.ObjectKinds!, cancellationToken);
            JsonElement readback = Data(await api.TryAsync(_progress, group, project, JsonSerializer.SerializeToElement(readParameters), dry_run: false, cancellationToken: cancellationToken));
            string? actual = readback.GetProperty("value").GetString();
            writeMatches = actual == Convert.ToString(parameters["value"], CultureInfo.InvariantCulture);
            if (actual == originalValue && !result.Changed)
            { result.Outcome = "failed"; result.UndoRestores = false; return; }
        }
        if (entry.Kind == "api-write" && result.Outcome == "ok" || entry.Kind == "action" && result.Outcome == "ok" && result.Changed)
        {
            // One undo only, and only while LargeWrite remains the sole current project (A41).
            Assert.Equal([project], await alone.OpenPathsAsync());
            await alone.UndoAsync().WaitAsync(cancellationToken);
            result.UndoRestores = before == (await PagesAsync(bridge, project, cancellationToken)).Hash;
            if (propertyWrite)
            {
                parameters.Remove("value");
                parameters["target"] = await LocatorAsync(bridge, project, entry.ObjectKinds!, cancellationToken);
                JsonElement restored = Data(await api.TryAsync(_progress, group, project, JsonSerializer.SerializeToElement(parameters), dry_run: false, cancellationToken: cancellationToken));
                result.UndoRestores &= originalValue == restored.GetProperty("value").GetString();
            }
            else
            { result.UndoRestores &= SameDesign(design, LiveSnapshots.DesignFiles(project)); }
            if (result.UndoRestores != true)
            { result.Outcome = "failed"; }
        }
        if (result.Kind == "api-read" && result.Changed)
        { result.Outcome = "failed"; }
        if (!writeMatches)
        { result.Outcome = "failed"; }
        result.EplanMessages = Math.Max(0, await MessagesAsync(bridge, cancellationToken) - _messageBookmark);
        BatchValues.Classify(result);
    }

    private async Task<JsonElement?> LocatorAsync(IBridgeClient bridge, string project, string[] kinds, CancellationToken cancellationToken)
    {
        // Exercise eplan_object_find's implementation, including schema and locator shaping.
        foreach (string kind in kinds)
        {
            JsonElement found = Data(await ObjectReadTools.ReadAsync(bridge, _paging, false,
                JsonSerializer.SerializeToElement(new { project, object_type = kind, limit = 1 }), cancellationToken));
            if (found.GetProperty("items").EnumerateArray().Select(i => (JsonElement?)i.GetProperty("locator").Clone()).FirstOrDefault() is { } locator)
            { return locator; }
        }
        return null;
    }
    private static async Task<LiveSnapshots.Snapshot> PagesAsync(IBridgeClient bridge, string project, CancellationToken cancellationToken)
    {
        // LiveSnapshots takes the concrete production client; no alternate transport is used here.
        return await LiveSnapshots.TakeCancellableAsync((BridgeClient)bridge, new() { Project = project }, cancellationToken);
    }
    private static async Task<int> MessagesAsync(IBridgeClient bridge, CancellationToken cancellationToken) =>
        (await bridge.InvokeAsync<MessageListArgs, MessageListData>(OperationNames.MessageList, new() { MinLevel = SystemMessage.Message, Limit = 1 }, cancellationToken)).Total;
    private static bool SameDesign(Dictionary<string, string> left, Dictionary<string, string> right) =>
        left.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(right.OrderBy(p => p.Key, StringComparer.Ordinal));
    private static JsonElement Payload(CallToolResult call) => call.IsError == true
        ? JsonDocument.Parse(string.Join("\n", call.Content.OfType<TextContentBlock>().Select(t => t.Text))).RootElement.GetProperty("error").Clone()
        : call.StructuredContent!.Value;
    private static JsonElement Data(CallToolResult call)
    {
        JsonElement data = Payload(call);
        if (call.IsError == true)
        { throw new BridgeCallException(data.Deserialize<BridgeError>(HostJson.Options)!); }
        return data;
    }
    private static void Error(BatchObservation result, BridgeError error)
    {
        result.Detail = error.Reason + ": " + error.Message;
        result.Outcome = error.Reason switch
        {
            ErrorReason.EplanBusy => "modal",
            ErrorReason.Timeout => "timeout",
            ErrorReason.OutcomeUnknown or ErrorReason.WritesSuspended => "outcome_unknown",
            _ => error.Execution switch { "unknown" => "outcome_unknown", "rolled_back" => "rolled_back", _ => "failed" },
        };
    }
    private void Suspend(string project, ServiceProvider provider, string id)
    {
        DateTimeOffset started = ProcessStart();
        foreach (HostDataLocations locations in new[] { provider.GetRequiredService<HostDataLocations>(), HostDataLocations.FromEnvironment() })
        {
            SuspensionStore.For(locations).Suspend(project, eplan.ProcessId, started, "BatchVerification", Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow, SuspensionReason.OutcomeUnknown, [id]);
        }
    }
    private DateTimeOffset ProcessStart()
    {
        try
        {
            using Process process = Process.GetProcessById(eplan.ProcessId);
            return process.StartTime.ToUniversalTime();
        }
        catch (ArgumentException)
        {
            // A crashed instance still requires a persistent project suspension.
            return DateTimeOffset.MinValue;
        }
    }
    private bool ProcessAlive()
    {
        try
        {
            using Process process = Process.GetProcessById(eplan.ProcessId);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }
    private void Save()
    {
        string temporary = _file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            date = _date,
            eplan = _version,
            stopped_at = _stoppedAt,
            stop_reason = _stopReason,
            writes_paused = _stopReason == "in_progress" || _results.Any(BatchValues.Stops),
            expected = _expected,
            entries = _results
        }, _json));
        // Live 2026-11-03: a scanner holding results.json for a moment denied the move and ended the whole run.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, _file, overwrite: true);
                return;
            }
            catch (Exception error) when (attempt < 10 && error is UnauthorizedAccessException or IOException)
            { Thread.Sleep(500); }
        }
    }
    private static bool InRepository(string path) => Repository(path) is not null;
    private static string? Repository(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        { if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx"))) { return directory.FullName; } }
        return null;
    }
}

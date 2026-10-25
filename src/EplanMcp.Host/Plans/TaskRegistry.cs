using System.Runtime.CompilerServices;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Http;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Plans;

/// <summary>
/// Host-only storage. The lock owns all entries, admission and project claims; callers receive immutable snapshots.
/// Session dictionaries are selected before ID lookup, so neither IDs nor client-supplied names grant access.
/// No request cancellation token is retained: producers own execution and safe stopping (ADR-0010).
/// </summary>
internal sealed class TaskRegistry : IDisposable
{
    public const int MaxActive = 8;
    public const int MaxTerminal = 256;
    private static readonly string[] _missingStableSession = ["transport:stable_session"];
    private readonly Lock _lock = new();
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly ConditionalWeakTable<McpServer, Session> _sessions = new();
    private readonly List<Entry> _entries = [];
    private readonly TimeProvider _clock;
    private readonly HttpCaller? _http;
    private readonly ITimer _cleanup;

    public TaskRegistry(TimeProvider clock, HttpCaller? http = null)
    {
        _clock = clock;
        _http = http;
        _cleanup = clock.CreateTimer(_ => Cleanup(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    // STDIO has no SDK SessionId. The DI server represents its connection; request facades do not.
    public PlanOwner OwnerFor(McpServer server) => SessionFor(server).Owner;

    /// <summary>Project is the canonical extensionless path used by the plan and WorkspaceStore (design 4.3).</summary>
    public TaskRecord Create(McpServer server, string planId, string project, int totalSteps)
    {
        Session session = SessionFor(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentOutOfRangeException.ThrowIfNegative(totalSteps);
        lock (_lock)
        {
            Cleanup();
            int active = _entries.Count(e => !e.Record.IsTerminal);
            if (active >= MaxActive)
            {
                throw Capacity("tasks:active", MaxActive, active);
            }

            // Reserve a terminal slot for every active task; completion must never evict an unexpired result.
            if (_entries.Count >= MaxTerminal)
            {
                throw Capacity("tasks:terminal", MaxTerminal, _entries.Count);
            }

            DateTimeOffset now = _clock.GetUtcNow();
            var record = new TaskRecord(Guid.NewGuid().ToString("N"), "plan_apply", planId, TaskStatus.Queued,
                new("Queued", 0, totalSteps), null, null, now, now, null, null, null);
            var entry = new Entry(session, project, record);
            session.Tasks.Add(record.TaskId, entry);
            _entries.Add(entry);
            return record;
        }
    }

    public TaskRecord Get(McpServer server, string taskId)
    {
        lock (_lock)
        {
            return Find(server, taskId).Record;
        }
    }

    /// <summary>Claims a project's executor once. Other producers leave their records queued and try again later.</summary>
    public bool TryStart(McpServer server, string taskId)
    {
        lock (_lock)
        {
            Entry entry = Find(server, taskId);
            if (entry.Record.Status != TaskStatus.Queued || _entries.TakeWhile(e => e != entry)
                .Any(e => !e.Record.IsTerminal && SameProject(e, entry))
                || _entries.Any(e => e.Record.StartedAt is not null && !e.Record.IsTerminal && SameProject(e, entry)))
            {
                return false;
            }

            DateTimeOffset now = _clock.GetUtcNow();
            entry.Record = entry.Record with { Status = TaskStatus.Running, Progress = entry.Record.Progress with { Message = "Running" }, StartedAt = now, UpdatedAt = now };
            return true;
        }
    }

    public void Update(McpServer server, string taskId, TaskProgress progress, bool waitingApproval = false)
    {
        lock (_lock)
        {
            Entry entry = Find(server, taskId);
            if (entry.Record.IsTerminal)
            {
                return;
            }

            if (entry.Record.StartedAt is null)
            {
                throw new InvalidOperationException("Claim the project executor before reporting progress.");
            }

            if (progress.TotalSteps != entry.Record.Progress.TotalSteps || progress.CompletedSteps < entry.Record.Progress.CompletedSteps
                || progress.CompletedSteps > progress.TotalSteps)
            {
                throw new ArgumentOutOfRangeException(nameof(progress));
            }

            progress = progress with { Message = ResultShaper.CleanText(progress.Message) };
            TaskStatus status = waitingApproval ? TaskStatus.WaitingApproval : TaskStatus.Running;
            if (entry.Record.Status != status || entry.Record.Progress != progress)
            {
                entry.Record = entry.Record with { Status = status, Progress = progress, UpdatedAt = _clock.GetUtcNow() };
            }
        }
    }

    /// <summary>Called only after the producer has settled write outcomes. A late cancellation cannot change a terminal record.</summary>
    public TaskRecord Complete(McpServer server, string taskId, PlanResult result, long revision,
        bool cancelled = false, bool interrupted = false)
    {
        lock (_lock)
        {
            Entry entry = Find(server, taskId);
            if (entry.Record.IsTerminal)
            {
                return entry.Record;
            }

            DateTimeOffset now = _clock.GetUtcNow();
            TaskError? error = null;
            if (result.Error is { } failure)
            {
                error = new(failure.Reason, failure.Message, result.Retryable, failure.Metadata, [], Guid.NewGuid().ToString("N"),
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(result.Execution.ToString()));
            }

            entry.Record = entry.Record with
            {
                Status = TaskRecord.StatusFor(result, cancelled, interrupted),
                Result = new(entry.Record.PlanId, revision, $"eplanmcp://plans/{entry.Record.PlanId}"),
                Error = error,
                Progress = entry.Record.Progress with { CompletedSteps = result.Steps.Count(s => s.Effective && s.Execution == ExecutionState.Committed) },
                UpdatedAt = now,
                CompletedAt = now,
                ExpiresAt = now.AddHours(1),
            };
            entry.Done.TrySetResult(entry.Record);
            return entry.Record;
        }
    }

    public async Task<TaskRecord> WaitAsync(McpServer server, string taskId, int waitSeconds = 90,
        CallProgress? progress = null, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_lock)
        {
            entry = Find(server, taskId);
        }

        if (waitSeconds is < 0 or > 110)
        {
            throw new ArgumentOutOfRangeException(nameof(waitSeconds));
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            if (entry.Record.IsTerminal || waitSeconds == 0)
            {
                return entry.Record;
            }
        }

        long started = _clock.GetTimestamp();
        double lastBeat = 0;
        bool stopped = false;
        IProgress<ProgressNotificationValue>? lane = progress?.Lane();
        using ITimer? heartbeat = lane is null ? null : _clock.CreateTimer(_ =>
        {
            lock (_lock)
            {
                double seconds = _clock.GetElapsedTime(started).TotalSeconds;
                int interval = entry.Record.Status == TaskStatus.WaitingApproval ? 15 : 5;
                if (stopped || cancellationToken.IsCancellationRequested || entry.Record.IsTerminal
                    || seconds >= waitSeconds || seconds - lastBeat < interval)
                {
                    return;
                }

                lastBeat = seconds;
                lane.Report(new ProgressNotificationValue { Progress = (float)seconds, Message = entry.Record.Progress.Message });
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        try
        {
            return await entry.Done.Task.WaitAsync(TimeSpan.FromSeconds(waitSeconds), _clock, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Get(server, taskId);
        }
        finally
        {
            lock (_lock)
            {
                stopped = true;
            }
        }
    }

    public void Dispose() => _cleanup.Dispose();

    private Session SessionFor(McpServer server)
    {
        if (_http is not null)
        {
            throw Failure(ErrorReason.CapabilityMissing, "This transport has no stable session. Use STDIO for plan tasks.", new { missing = _missingStableSession });
        }

        McpServer connection = server.Services?.GetService(typeof(McpServer)) as McpServer ?? server;
        return _sessions.GetValue(connection, _ => new(new(_runId, Guid.NewGuid().ToString("N"))));
    }

    private Entry Find(McpServer server, string taskId)
    {
        Session session = SessionFor(server); // Select the owner before looking up any task ID.
        if (!session.Tasks.TryGetValue(taskId, out Entry? entry) || entry.Record.ExpiresAt <= _clock.GetUtcNow())
        {
            throw Failure(ErrorReason.ResourceNotFound, "This task is unavailable in this Host session. Check the original operation's result; do not repeat a write just to obtain a new task ID.");
        }

        return entry;
    }

    private void Cleanup()
    {
        lock (_lock)
        {
            foreach (Entry entry in _entries.Where(e => e.Record.ExpiresAt <= _clock.GetUtcNow()).ToArray())
            {
                entry.Session.Tasks.Remove(entry.Record.TaskId);
                _entries.Remove(entry);
            }
        }
    }

    private static bool SameProject(Entry left, Entry right) => string.Equals(left.Project, right.Project, StringComparison.OrdinalIgnoreCase);
    private static TaskRegistryException Capacity(string resource, int limit, int used) =>
        Failure(ErrorReason.ResourceExhausted, "Task capacity is full. Check existing tasks before creating a new plan.", new { resource, limit, used });
    private static TaskRegistryException Failure(string reason, string message, object? metadata = null) =>
        new(new(reason, message, false, metadata is null ? null : JsonSerializer.SerializeToElement(metadata, HostJson.Options), [], Guid.NewGuid().ToString("N")));

    private sealed class Session(PlanOwner owner)
    {
        public PlanOwner Owner { get; } = owner;
        public Dictionary<string, Entry> Tasks { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Entry(Session session, string project, TaskRecord record)
    {
        public Session Session { get; } = session;
        public string Project { get; } = project;
        public TaskRecord Record { get; set; } = record;
        public TaskCompletionSource<TaskRecord> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

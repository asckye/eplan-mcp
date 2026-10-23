using System.Collections.Concurrent;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using ModelContextProtocol.Server;
using TaskStatus = EplanMcp.Host.Plans.TaskStatus;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class TaskRegistryWaitTests : IAsyncDisposable
{
    private readonly ManualTime _clock = TaskTestData.Clock();
    private readonly McpServer _server = TaskTestData.Server();

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    [Fact]
    public async Task Wait_TerminalWakesAllWaiters_ButIntermediateStatesAndProgressDoNot()
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        Task<TaskRecord> first = WaitAsync(registry, task.TaskId);
        Task<TaskRecord> second = WaitAsync(registry, task.TaskId, 110);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(registry.TryStart(_server, task.TaskId));
        registry.Update(_server, task.TaskId, new("Waiting for approval", 0, 1), true);
        _clock.Advance(TimeSpan.FromSeconds(15));
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        registry.Update(_server, task.TaskId, new("Verifying the committed step", 1, 1));
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        TaskRecord terminal = registry.Complete(_server, task.TaskId, TaskTestData.Result(), 1);
        Assert.Same(terminal, await Bounded(first));
        Assert.Same(terminal, await Bounded(second));
        Assert.True(WaitAsync(registry, task.TaskId).IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(110)]
    public async Task Wait_WindowEndsNormally_AndTaskCanCompleteAfterTheReceipt(int seconds)
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        Task<TaskRecord> wait = seconds == 90 ? WaitAsync(registry, task.TaskId) : WaitAsync(registry, task.TaskId, seconds);
        _clock.Advance(TimeSpan.FromSeconds(seconds - 1));
        Assert.False(wait.IsCompleted);
        registry.TryStart(_server, task.TaskId);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TaskStatus.Running, (await Bounded(wait)).Status);
        TaskRecord terminal = registry.Complete(_server, task.TaskId, TaskTestData.Result(), 1);
        Assert.Same(terminal, await WaitAsync(registry, task.TaskId, 0));
    }

    [Fact]
    public async Task Wait_ZeroIsImmediate_AndWindowValidationDoesNotTouchTask()
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        Assert.Same(task, await WaitAsync(registry, task.TaskId, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => WaitAsync(registry, task.TaskId, -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => WaitAsync(registry, task.TaskId, 111));
        Assert.Same(task, registry.Get(_server, task.TaskId));
        await using McpServer other = TaskTestData.Server();
        TaskRegistryException hidden = await Assert.ThrowsAsync<TaskRegistryException>(() => registry.WaitAsync(other, task.TaskId, 111, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.ResourceNotFound, hidden.Error.Reason); // Ownership precedes even window validation.
    }

    [Fact]
    public async Task Wait_CancellationStopsOnlyThatWait_ProducerAndOtherWaiterContinue()
    {
        using var registry = new TaskRegistry(_clock);
        using var cancelWait = new CancellationTokenSource();
        var client = new RecordedProgress();
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        registry.TryStart(_server, task.TaskId);
        Task<TaskRecord> cancelled = registry.WaitAsync(_server, task.TaskId, progress: CallProgress.For(client), cancellationToken: cancelWait.Token);
        Task<TaskRecord> observer = WaitAsync(registry, task.TaskId);
        _clock.Advance(TimeSpan.FromSeconds(5));
        cancelWait.Cancel();
        _clock.Advance(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bounded(cancelled));
        Assert.Single(client.Values);
        Assert.False(observer.IsCompleted);
        Assert.Equal(TaskStatus.Running, registry.Get(_server, task.TaskId).Status);
        TaskRecord terminal = registry.Complete(_server, task.TaskId, TaskTestData.Result(), 1);
        Assert.Same(terminal, await Bounded(observer));
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(client.Values);
    }

    [Fact]
    public async Task Heartbeat_UsesFiveOrFifteenSeconds_StaysMonotonicAcrossLanes_AndNeverRefreshesUpdatedAt()
    {
        using var registry = new TaskRegistry(_clock);
        var client = new RecordedProgress();
        CallProgress call = CallProgress.For(client)!;
        call.Lane().Report(new() { Progress = 100, Message = "Previous wait" });
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        registry.TryStart(_server, task.TaskId);
        Task<TaskRecord> wait = WaitAsync(registry, task.TaskId, 60, call);
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal([100f, 106f, 111f], client.Values.Select(v => v.Progress));
        registry.Update(_server, task.TaskId, new("Waiting\u0007 for approval", 0, 1), true);
        TaskRecord approval = registry.Get(_server, task.TaskId);
        _clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(3, client.Values.Count);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(126f, client.Values[^1].Progress);
        Assert.Equal("Waiting for approval", client.Values[^1].Message);
        Assert.Same(approval, registry.Get(_server, task.TaskId));
        registry.Update(_server, task.TaskId, new("Running", 0, 1));
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(131f, client.Values[^1].Progress);
        Assert.All(client.Values, v => Assert.Null(v.Total));
        registry.Complete(_server, task.TaskId, TaskTestData.Result(), 1);
        int reports = client.Values.Count;
        _clock.Advance(TimeSpan.FromSeconds(20));
        await Bounded(wait);
        Assert.Equal(reports, client.Values.Count);
    }

    [Fact]
    public async Task Heartbeat_WindowEndDisposesTheHook_AndNextGetUsesItsOwnCallProgress()
    {
        using var registry = new TaskRegistry(_clock);
        var oldClient = new RecordedProgress();
        var nextClient = new RecordedProgress();
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        Task<TaskRecord> first = WaitAsync(registry, task.TaskId, 10, CallProgress.For(oldClient));
        _clock.Advance(TimeSpan.FromSeconds(10));
        await Bounded(first);
        Task<TaskRecord> second = WaitAsync(registry, task.TaskId, 10, CallProgress.For(nextClient));
        _clock.Advance(TimeSpan.FromSeconds(10));
        await Bounded(second);
        Assert.Equal([5f], oldClient.Values.Select(v => v.Progress));
        Assert.Equal([5f], nextClient.Values.Select(v => v.Progress));
        Assert.Same(task, registry.Get(_server, task.TaskId));
    }

    [Fact]
    public async Task Capacity_EightActiveIncludesQueued_RefusalDoesNotEvict_AndCompletionFreesAdmission()
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord[] tasks = [.. Enumerable.Range(0, TaskRegistry.MaxActive).Select(_ => registry.Create(_server, "plan", "PROJECT", 1))];
        registry.TryStart(_server, tasks[0].TaskId);
        registry.Update(_server, tasks[0].TaskId, new("Waiting for approval", 0, 1), true);
        Task<TaskRecord> wait = WaitAsync(registry, tasks[0].TaskId);
        TaskRegistryTests.Capacity(() => registry.Create(_server, "refused", "OTHER", 1), "tasks:active", 8, 8);
        Assert.All(tasks, t => Assert.Equal(t.TaskId, registry.Get(_server, t.TaskId).TaskId));
        Assert.False(wait.IsCompleted);
        registry.Complete(_server, tasks[0].TaskId, TaskTestData.Result(), 1);
        await Bounded(wait);
        Assert.Equal(TaskStatus.Queued, registry.Create(_server, "admitted", "OTHER", 1).Status);
    }

    [Fact]
    public void ConcurrentAdmissionAndClaims_KeepEightActiveAndExactlyOneProjectExecutor()
    {
        using var registry = new TaskRegistry(_clock);
        var admitted = new ConcurrentBag<TaskRecord>();
        var refused = new ConcurrentBag<TaskError>();
        Parallel.For(0, 32, _ =>
        {
            try
            { admitted.Add(registry.Create(_server, "plan", "PROJECT", 1)); }
            catch (TaskRegistryException exception) { refused.Add(exception.Error); }
        });
        Assert.Equal(8, admitted.Count);
        Assert.Equal(24, refused.Count);
        Assert.All(refused, error => Assert.Equal(ErrorReason.ResourceExhausted, error.Reason));
        int executors = 0;
        Parallel.ForEach(admitted, task =>
        {
            if (registry.TryStart(_server, task.TaskId))
            { Interlocked.Increment(ref executors); }
        });
        Assert.Equal(1, executors);
    }

    [Fact]
    public async Task Heartbeat_WallClockMovingBackwards_DoesNotMoveElapsedSecondsBackwards()
    {
        var wallClock = new JumpingClock(_clock);
        using var registry = new TaskRegistry(wallClock);
        var client = new RecordedProgress();
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        Task<TaskRecord> wait = registry.WaitAsync(_server, task.TaskId, 15, CallProgress.For(client), TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(5));
        wallClock.Offset = TimeSpan.FromHours(-2);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await Bounded(wait);
        Assert.Equal([5f, 10f], client.Values.Select(v => v.Progress));
        Assert.Same(task, registry.Get(_server, task.TaskId));
    }

    private static Task<TaskRecord> Bounded(Task<TaskRecord> task) => task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

    private Task<TaskRecord> WaitAsync(TaskRegistry registry, string taskId, int seconds = 90, CallProgress? progress = null) =>
        registry.WaitAsync(_server, taskId, seconds, progress, TestContext.Current.CancellationToken);

    private sealed class JumpingClock(ManualTime time) : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => time.Now + Offset;
        public override long GetTimestamp() => time.GetTimestamp();
        public override long TimestampFrequency => time.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            time.CreateTimer(callback, state, dueTime, period);
    }
}

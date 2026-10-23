using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Safety;
using ModelContextProtocol.Server;
using TaskStatus = EplanMcp.Host.Plans.TaskStatus;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class TaskRegistryTests : IAsyncDisposable
{
    private readonly ManualTime _clock = TaskTestData.Clock();
    private readonly McpServer _server = TaskTestData.Server();

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    [Fact]
    public async Task Ownership_DifferentSessionReconnectAndHost_AllLookLikeMissing()
    {
        using var registry = new TaskRegistry(_clock);
        using var otherHost = new TaskRegistry(_clock);
        await using McpServer reconnected = TaskTestData.Server();
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);

        Assert.Same(task, registry.Get(_server, task.TaskId));
        PlanOwner owner = registry.OwnerFor(_server);
        Assert.Equal(owner, registry.OwnerFor(_server));
        Assert.Matches("^[0-9a-f]{32}$", owner.RunId);
        Assert.Matches("^[0-9a-f]{32}$", owner.SessionId);
        Assert.NotEqual(owner.SessionId, registry.OwnerFor(reconnected).SessionId);
        Assert.NotEqual(owner.RunId, otherHost.OwnerFor(_server).RunId);
        TaskError unknown = Missing(() => registry.Get(_server, "missing"));
        foreach (TaskError hidden in new[] { Missing(() => registry.Get(reconnected, task.TaskId)), Missing(() => otherHost.Get(_server, task.TaskId)) })
        {
            Assert.Equal(unknown.Reason, hidden.Reason);
            Assert.Equal(unknown.Message, hidden.Message);
            Assert.Null(hidden.Metadata);
        }
        Assert.Equal(ErrorReason.ResourceNotFound, Assert.Throws<TaskRegistryException>(() => registry.TryStart(reconnected, task.TaskId)).Error.Reason);
        Assert.Equal(ErrorReason.ResourceNotFound, Assert.Throws<TaskRegistryException>(() => registry.Update(reconnected, task.TaskId, new("Hidden", 0, 1))).Error.Reason);
        Assert.Equal(ErrorReason.ResourceNotFound, Assert.Throws<TaskRegistryException>(() => registry.Complete(reconnected, task.TaskId, TaskTestData.Result(), 1)).Error.Reason);
    }

    [Fact]
    public void Timestamps_ReadsAndRepeatedProgressNeverRefresh_AndLateCancellationCannotChangeTerminal()
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord queued = registry.Create(_server, "plan", "PROJECT", 1);
        Assert.Null(queued.StartedAt);
        Assert.Null(queued.CompletedAt);
        Assert.Null(queued.ExpiresAt);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Same(queued, registry.Get(_server, queued.TaskId));
        Assert.True(registry.TryStart(_server, queued.TaskId));
        Assert.False(registry.TryStart(_server, queued.TaskId));
        registry.Update(_server, queued.TaskId, new("Waiting for approval", 0, 1), waitingApproval: true);
        TaskRecord waiting = registry.Get(_server, queued.TaskId);
        Assert.Equal(_clock.Now, waiting.StartedAt);
        _clock.Advance(TimeSpan.FromMinutes(1));
        registry.Update(_server, queued.TaskId, waiting.Progress, waitingApproval: true);
        Assert.Same(waiting, registry.Get(_server, queued.TaskId));
        TaskRecord done = registry.Complete(_server, queued.TaskId, TaskTestData.Result(), 3);
        Assert.Equal(_clock.Now, done.CompletedAt);
        Assert.Equal(_clock.Now.AddHours(1), done.ExpiresAt);
        Assert.Equal(1, done.Progress.CompletedSteps);
        Assert.Same(done, registry.Complete(_server, queued.TaskId, TaskTestData.Result(), 4, cancelled: true));
        registry.Update(_server, queued.TaskId, new("Late progress", 1, 1));
        Assert.Same(done, registry.Get(_server, queued.TaskId));
        Assert.Equal(TaskStatus.Queued, queued.Status); // Earlier snapshots remain detached.
    }

    [Fact]
    public void Expiration_IsAnHourAfterCompletion_ReadsNeverRenew_AndActiveTasksNeverExpire()
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord active = registry.Create(_server, "active", "OTHER", 1);
        TaskRecord terminal = registry.Create(_server, "done", "PROJECT", 1);
        _clock.Advance(TimeSpan.FromHours(2));
        TaskRecord done = registry.Complete(_server, terminal.TaskId, TaskTestData.Result(), 1);
        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Same(done, registry.Get(_server, terminal.TaskId));
        _clock.Advance(TimeSpan.FromMinutes(1));
        Missing(() => registry.Get(_server, terminal.TaskId));
        Assert.Same(active, registry.Get(_server, active.TaskId));
    }

    [Fact]
    public async Task ProjectClaim_QueuesInAdmissionOrderAcrossSessions_AndAllowsOtherProjects()
    {
        using var registry = new TaskRegistry(_clock);
        await using McpServer otherSession = TaskTestData.Server();
        TaskRecord first = registry.Create(_server, "first", "PROJECT", 1);
        TaskRecord next = registry.Create(otherSession, "next", "project", 1);
        TaskRecord other = registry.Create(_server, "other", "OTHER", 1);
        Assert.False(registry.TryStart(otherSession, next.TaskId));
        Assert.True(registry.TryStart(_server, first.TaskId));
        registry.Update(_server, first.TaskId, new("Waiting for approval", 0, 1), true);
        Assert.False(registry.TryStart(otherSession, next.TaskId));
        Assert.Null(registry.Get(otherSession, next.TaskId).StartedAt);
        Assert.True(registry.TryStart(_server, other.TaskId));
        registry.Complete(_server, first.TaskId, TaskTestData.Result(), 1);
        Assert.True(registry.TryStart(otherSession, next.TaskId));
    }

    [Fact]
    public void TerminalCapacity_ReservesSlotsForActiveTasks_AndNeverEvictsUnexpiredResults()
    {
        using var registry = new TaskRegistry(_clock);
        _clock.Advance(TimeSpan.FromSeconds(30)); // Expiry falls between minute cleanup ticks; admission must clean too.
        TaskRecord first = registry.Create(_server, "first", "PROJECT", 1);
        registry.Complete(_server, first.TaskId, TaskTestData.Result(), 1);
        for (int i = 1; i < TaskRegistry.MaxTerminal - 1; i++)
        {
            TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
            registry.Complete(_server, task.TaskId, TaskTestData.Result(), 1);
        }
        TaskRecord reserved = registry.Create(_server, "active", "PROJECT", 1);
        Capacity(() => registry.Create(_server, "refused", "PROJECT", 1), "tasks:terminal", 256, 256);
        registry.Complete(_server, reserved.TaskId, TaskTestData.Result(), 1);
        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(TaskStatus.Succeeded, registry.Get(_server, first.TaskId).Status);
        Capacity(() => registry.Create(_server, "refused", "PROJECT", 1), "tasks:terminal", 256, 256);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TaskStatus.Queued, registry.Create(_server, "admitted", "PROJECT", 1).Status);
        Missing(() => registry.Get(_server, first.TaskId));
    }

    [Fact]
    public void Record_HasExactlyTheContractFields_AndPreservesPartialFailureAndReference()
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 2);
        PlanResult partial = PlanResult.Aggregate([new(0, "page.update", true, ExecutionState.Committed), new(1, "page.update", true, ExecutionState.RolledBack)],
            new(ErrorReason.Internal, "Check the plan.", false, PlanTestData.Json("{\"detail\":1}"))) with
        { Verification = "failed" };
        TaskRecord done = registry.Complete(_server, task.TaskId, partial, 42);
        JsonElement json = JsonSerializer.SerializeToElement(done, HostJson.Options);
        Assert.Equal(["taskId", "kind", "planId", "status", "progress", "result", "error", "createdAt", "updatedAt", "startedAt", "completedAt", "expiresAt"], json.EnumerateObject().Select(p => p.Name));
        Assert.Equal("plan_apply", done.Kind);
        Assert.Equal("failed", json.GetProperty("status").GetString());
        Assert.Equal(new TaskResultReference("plan", 42, "eplanmcp://plans/plan"), done.Result);
        Assert.Equal("partially_committed", done.Error!.Execution);
        Assert.Equal(1, done.Error.Metadata!.Value.GetProperty("detail").GetInt32());
        Assert.False(done.Error.Retryable);
        Assert.Equal(1, done.Progress.CompletedSteps);
        Assert.False(json.TryGetProperty("execution", out _));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Complete_UnknownWinsOverCancellationAndInterruption_AndPreservesItsNonretryableError(bool cancelled, bool interrupted)
    {
        using var registry = new TaskRegistry(_clock);
        TaskRecord task = registry.Create(_server, "plan", "PROJECT", 1);
        TaskRecord done = registry.Complete(_server, task.TaskId, TaskTestData.Result(ExecutionState.Unknown, "skipped", ErrorReason.OutcomeUnknown), 1, cancelled, interrupted);
        Assert.Equal(TaskStatus.Unknown, done.Status);
        Assert.Equal(ErrorReason.OutcomeUnknown, done.Error!.Reason);
        Assert.Equal(ExecutionState.Unknown, done.Error.Execution);
        Assert.False(done.Error.Retryable);
        Assert.Empty(done.Error.EplanMessages);
        Assert.NotEmpty(done.Error.CorrelationId);
        Assert.NotNull(done.Result);
        TaskError readError = Missing(() => registry.Get(_server, "missing"));
        Assert.False(JsonSerializer.SerializeToElement(readError, HostJson.Options).TryGetProperty("execution", out _));
    }

    internal static TaskError Missing(Action action)
    {
        TaskError error = Assert.Throws<TaskRegistryException>(action).Error;
        Assert.Equal(ErrorReason.ResourceNotFound, error.Reason);
        return error;
    }

    internal static void Capacity(Action action, string resource, int limit, int used)
    {
        TaskError error = Assert.Throws<TaskRegistryException>(action).Error;
        Assert.Equal(ErrorReason.ResourceExhausted, error.Reason);
        Assert.False(error.Retryable);
        Assert.Equal(resource, error.Metadata!.Value.GetProperty("resource").GetString());
        Assert.Equal(limit, error.Metadata.Value.GetProperty("limit").GetInt32());
        Assert.Equal(used, error.Metadata.Value.GetProperty("used").GetInt32());
        Assert.Equal(3, error.Metadata.Value.EnumerateObject().Count());
    }
}

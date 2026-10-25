using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlanApplyProtocolTests
{
    private static readonly string[] _progressTokens = ["apply", "get"];

    [Fact]
    public async Task ClientDisconnect_StopsWaiting_AndWorkerCompletesWithOriginalTask()
    {
        _bridge.BackupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithClient(async (client, services) =>
        {
            string id = await Compile(client);
            Task<CallToolResult> applying = Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id });
            await _bridge.BackupEntered.Task.WaitAsync(Token);
            string taskId = (await services.GetRequiredService<PlanStore>().ReadAsync(id, Token))!.State.TaskId!;
            await client.DisposeAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => applying);
            Assert.False(_bridge.WorkerToken.IsCancellationRequested);
            _bridge.BackupGate.TrySetResult();
            TaskRecord terminal = await services.GetRequiredService<TaskRegistry>().WaitAsync(_server!, taskId, cancellationToken: Token);
            Assert.Equal(EplanMcp.Host.Plans.TaskStatus.Succeeded, terminal.Status);
            Assert.Equal(2, _fake.Writes.Count);
        });
    }

    [Fact]
    public async Task SecondStepFailure_ApplyKeepsCommittedStepAndBackup_TaskGetIsSuccessfulQuery()
    {
        int writes = 0;
        _fake.DuringWrite = _ =>
        {
            if (++writes == 2)
            {
                _fake.WriteFailure = new() { Reason = ErrorReason.LockFailed, Message = "Locked; inspect the plan before continuing.", Execution = ExecutionState.RolledBack };
            }
        };
        await WithClient(async (client, services) =>
        {
            string id = await Compile(client);
            CallToolResult call = await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id });
            Assert.True(call.IsError);
            JsonElement result = call.StructuredContent!.Value;
            Assert.Equal("partially_committed", result.GetProperty("execution").GetString());
            Assert.Equal(1, Assert.Single(result.GetProperty("completedSteps").EnumerateArray()).GetInt32());
            Assert.Equal("rolled_back", result.GetProperty("steps")[1].GetProperty("execution").GetString());
            Assert.True(File.Exists(result.GetProperty("backup").GetString()));
            Assert.False(result.GetProperty("error").GetProperty("retryable").GetBoolean());
            string taskId = (await services.GetRequiredService<PlanStore>().ReadAsync(id, Token))!.State.TaskId!;
            JsonElement task = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId }));
            Assert.Equal("failed", task.GetProperty("status").GetString());
            Assert.Equal("partially_committed", task.GetProperty("error").GetProperty("execution").GetString());
        });
    }
    [Fact]
    public async Task CancelTaskGet_OnlyEndsWait_WorkerStillCommits()
    {
        _bridge.BackupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithClient(async (client, _) =>
        {
            string id = await Compile(client);
            JsonElement receipt = ContextToolTests.Data(await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id, ["wait_seconds"] = 0 }));
            string taskId = receipt.GetProperty("taskId").GetString()!;
            await _bridge.BackupEntered.Task.WaitAsync(Token);
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(Token);
            Task<CallToolResult> call = client.CallToolAsync(PlanApplyTools.TaskTool, new Dictionary<string, object?> { ["task_id"] = taskId }, cancellationToken: waiting.Token).AsTask();
            await waiting.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            Assert.False(_bridge.WorkerToken.IsCancellationRequested);
            _bridge.BackupGate.SetResult();
            JsonElement terminal = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId }));
            Assert.Equal("succeeded", terminal.GetProperty("status").GetString());
            Assert.Equal(2, _fake.Writes.Count);
        });
    }

    [Fact]
    public async Task ExplicitCancelApply_StopsAfterSettlingPreparation_AndPlanRemainsConsumed()
    {
        _bridge.BackupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithClient(async (client, services) =>
        {
            string id = await Compile(client);
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(Token);
            Task<CallToolResult> call = client.CallToolAsync(PlanApplyTools.ApplyTool, new Dictionary<string, object?> { ["plan_id"] = id }, cancellationToken: waiting.Token).AsTask();
            await _bridge.BackupEntered.Task.WaitAsync(Token);
            string taskId = (await services.GetRequiredService<PlanStore>().ReadAsync(id, Token))!.State.TaskId!;
            await client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = _requestId }, HostJson.Options, Token);
            await waiting.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            await AdvanceUntil(Task.Run(async () =>
            {
                while (!_bridge.WorkerToken.IsCancellationRequested)
                {
                    await Task.Delay(1, Token);
                }
            }, Token), TimeSpan.Zero);
            _bridge.BackupGate.TrySetResult();
            JsonElement terminal = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId }));
            Assert.Equal("cancelled", terminal.GetProperty("status").GetString());
            Assert.NotEqual(JsonValueKind.Null, terminal.GetProperty("error").ValueKind);
            Assert.Empty(_fake.Writes);
            Assert.NotNull((await services.GetRequiredService<PlanStore>().ReadAsync(id, Token))!.State.AttemptId);
        });
    }

    [Fact]
    public async Task ApplyProgress_StopsWithReceipt_TaskGetUsesItsOwnMonotonicProgress()
    {
        _bridge.BackupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithClient(async (client, _) =>
        {
            string id = await Compile(client);
            var notifications = new List<(string Token, float Progress)>();
            Task<CallToolResult> applying = client.CallToolAsync(PlanApplyTools.ApplyTool,
                new Dictionary<string, object?> { ["plan_id"] = id, ["wait_seconds"] = 10 },
                progress: new CallbackProgress("apply", notifications), cancellationToken: Token).AsTask();
            await _bridge.BackupEntered.Task.WaitAsync(Token);
            await AdvanceUntil(applying, TimeSpan.FromSeconds(5));
            string taskId = ContextToolTests.Data(await applying).GetProperty("taskId").GetString()!;
            Task<CallToolResult> getting = client.CallToolAsync(PlanApplyTools.TaskTool,
                new Dictionary<string, object?> { ["task_id"] = taskId, ["wait_seconds"] = 10 },
                progress: new CallbackProgress("get", notifications), cancellationToken: Token).AsTask();
            await AdvanceUntil(getting, TimeSpan.FromSeconds(5));
            await getting;
            lock (notifications)
            {
                foreach (string token in _progressTokens)
                {
                    float[] values = [.. notifications.Where(p => p.Token == token).Select(p => p.Progress)];
                    Assert.NotEmpty(values);
                    Assert.True(values.Zip(values.Skip(1)).All(p => p.First < p.Second));
                }
            }
            _bridge.BackupGate.SetResult();
            await Call(client, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId });
        });
    }

    [Theory]
    [InlineData(ErrorReason.PolicyDenied)]
    [InlineData(ErrorReason.BackupFailed)]
    [InlineData(ErrorReason.WritesSuspended)]
    [InlineData(ErrorReason.PlanStale)]
    public async Task AdmittedFailure_IsStructuredAndConsumed_AndTaskQueryStillSucceeds(string reason)
    {
        await WithClient(async (client, services) =>
        {
            string id = await Compile(client);
            if (reason == ErrorReason.PlanStale)
            {
                _clock.Advance(TimeSpan.FromHours(25));
            }
            else if (reason == ErrorReason.PolicyDenied)
            {
                // The host's configured scratch roots exclude this plan target.
            }
            else if (reason == ErrorReason.WritesSuspended)
            {
                services.GetRequiredService<SuspensionStore>().Suspend(_current.Project.Path, Environment.ProcessId,
                    SuspensionStore.StartOf(Environment.ProcessId)!.Value, "earlier", "request", _clock.Now, SuspensionReason.OutcomeUnknown, []);
            }
            else
            {
                _fake.Handlers[OperationNames.ProjectBackup] = _ => throw new EplanMcp.Host.Bridge.BridgeCallException(new BridgeError { Reason = ErrorReason.BackupFailed, Message = "Backup failed; inspect disk space." });
            }
            CallToolResult call = await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id });
            JsonElement error = ContextToolTests.Error(call);
            Assert.Equal(reason, error.GetProperty("reason").GetString());
            Assert.Equal("not_started", call.StructuredContent!.Value.GetProperty("execution").GetString());
            Assert.Equal(2, call.StructuredContent.Value.GetProperty("steps").GetArrayLength());
            string taskId = (await services.GetRequiredService<PlanStore>().ReadAsync(id, Token))!.State.TaskId!;
            JsonElement task = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId }));
            Assert.Equal("failed", task.GetProperty("status").GetString());
            Assert.Equal(reason, task.GetProperty("error").GetProperty("reason").GetString());
            Assert.Equal(ErrorReason.InvalidArgument, ContextToolTests.Error(await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id })).GetProperty("reason").GetString());
            Assert.Empty(_fake.Writes);
        }, services =>
        {
            if (reason == ErrorReason.PolicyDenied)
            {
                services.AddSingleton(HostConfig.Defaults(_locations) with { ScratchRoots = [Path.Combine(_locations.Root, "other")], WritesOutsideScratch = OutsideScratchWrites.Deny });
            }
        });
    }

    [Theory]
    [InlineData(PlanApplyTools.ApplyTool, "plan_id")]
    [InlineData(PlanApplyTools.TaskTool, "task_id")]
    public async Task InvalidInputs_ListAllViolationsBeforeBridgeCalls(string tool, string id)
    {
        await WithClient(async (client, _) =>
        {
            JsonElement error = ContextToolTests.Error(await Call(client, tool, new() { [id] = " ", ["wait_seconds"] = 1.5, ["resume"] = true }));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            Assert.Equal(3, error.GetProperty("metadata").GetProperty("violations").GetArrayLength());
            Assert.Empty(_fake.Calls);
        });
    }

    private sealed class CallbackProgress(string token, List<(string Token, float Progress)> notifications) : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value)
        {
            lock (notifications)
            {
                notifications.Add((token, value.Progress));
            }
        }
    }
}

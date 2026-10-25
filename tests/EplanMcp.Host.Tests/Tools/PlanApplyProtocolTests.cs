using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Trait("Tool", PlanApplyTools.ApplyTool)]
[Trait("Tool", PlanApplyTools.TaskTool)]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class PlanApplyProtocolTests : PlanProtocolFixture
{
    [Fact]
    public async Task Apply_WaitToTerminal_PreservesVerificationBackupAndBothJsonCopies()
    {
        await WithClient(async (client, _) =>
        {
            string id = await Compile(client);
            CallToolResult call = await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id });
            JsonElement result = ContextToolTests.Data(call);
            Assert.Equal("committed", result.GetProperty("execution").GetString());
            Assert.Equal("passed", result.GetProperty("verification").GetString());
            Assert.Equal(2, result.GetProperty("completedSteps").GetArrayLength());
            Assert.True(File.Exists(result.GetProperty("backup").GetString()));
            Assert.True(JsonElement.DeepEquals(result, JsonSerializer.Deserialize<JsonElement>(((TextContentBlock)call.Content[1]).Text)));
            Assert.Equal(2, _fake.Writes.Count);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Apply_ReceiptOrWaitTimeout_WorkerContinuesAndTaskGetReturnsTerminalReference(int wait)
    {
        _bridge.BackupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithClient(async (client, _) =>
        {
            string id = await Compile(client);
            Task<CallToolResult> applying = Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id, ["wait_seconds"] = wait });
            await _bridge.BackupEntered.Task.WaitAsync(Token);
            if (wait != 0)
            {
                await AdvanceUntil(applying, TimeSpan.FromSeconds(1));
            }
            JsonElement receipt = ContextToolTests.Data(await applying);
            Assert.Equal(id, receipt.GetProperty("planId").GetString());
            string taskId = receipt.GetProperty("taskId").GetString()!;
            Assert.False(_bridge.WorkerToken.IsCancellationRequested);
            JsonElement running = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId, ["wait_seconds"] = 0 }));
            Assert.Equal("running", running.GetProperty("status").GetString());
            _bridge.BackupGate.SetResult();
            JsonElement terminal = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId }));
            Assert.Equal("succeeded", terminal.GetProperty("status").GetString());
            Assert.Equal(id, terminal.GetProperty("result").GetProperty("planId").GetString());
            Assert.False(terminal.TryGetProperty("execution", out JsonElement _));
            ReadResourceResult resource = await client.ReadResourceAsync("eplanmcp://tasks/" + taskId, cancellationToken: Token);
            Assert.True(JsonElement.DeepEquals(terminal, JsonSerializer.Deserialize<JsonElement>(((TextResourceContents)resource.Contents[0]).Text)));
            Assert.Equal(2, _fake.Writes.Count);
        });
    }

    [Fact]
    public async Task DryRun_ConsumesNothing_ThenRealApplyAndReplayIsRefused()
    {
        await WithClient(async (client, services) =>
        {
            string id = await Compile(client);
            JsonElement preview = ContextToolTests.Data(await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id, ["dry_run"] = true }));
            Assert.Equal("not_started", preview.GetProperty("execution").GetString());
            Assert.NotEmpty(preview.GetProperty("steps")[0].GetProperty("changes").EnumerateArray());
            PlanStore store = services.GetRequiredService<PlanStore>();
            Assert.Null((await store.ReadAsync(id, Token))!.State.AttemptId);
            Assert.Empty(_fake.Writes);
            Assert.DoesNotContain(_fake.Calls, c => c.Operation == OperationNames.ProjectBackup);
            Assert.False(Directory.Exists(_locations.Backups));
            Assert.False((await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id })).IsError);
            JsonElement replay = ContextToolTests.Error(await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id }));
            Assert.Equal(ErrorReason.InvalidArgument, replay.GetProperty("reason").GetString());
            Assert.Contains("eplan_plan_get", replay.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(2, _fake.Writes.Count);
        });
    }

    [Fact]
    public async Task TaskGet_OtherSessionAndMissingId_AreIndistinguishable()
    {
        await WithClient(async (client, services) =>
        {
            string id = await Compile(client);
            await Call(client, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id });
            string taskId = (await services.GetRequiredService<PlanStore>().ReadAsync(id, Token))!.State.TaskId!;
            await WithClient(async (other, _) =>
            {
                JsonElement hidden = ContextToolTests.Error(await Call(other, PlanApplyTools.TaskTool, new() { ["task_id"] = taskId }));
                JsonElement missing = ContextToolTests.Error(await Call(other, PlanApplyTools.TaskTool, new() { ["task_id"] = "missing" }));
                Assert.Equal(ErrorReason.ResourceNotFound, hidden.GetProperty("reason").GetString());
                Assert.Equal(hidden.GetProperty("message").GetString(), missing.GetProperty("message").GetString());
                JsonElement plan = ContextToolTests.Error(await Call(other, PlanApplyTools.ApplyTool, new() { ["plan_id"] = id }));
                Assert.Equal(ErrorReason.ResourceNotFound, plan.GetProperty("reason").GetString());
            }, s => s.AddSingleton(services.GetRequiredService<TaskRegistry>()));
        });
    }

}

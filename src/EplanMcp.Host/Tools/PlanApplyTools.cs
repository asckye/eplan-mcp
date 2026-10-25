using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>The worker owns execution; request tokens only bound observation of its task.</summary>
[McpServerToolType]
[McpServerResourceType]
internal sealed class PlanApplyTools
{
    public const string ApplyTool = "eplan_plan_apply";
    public const string TaskTool = "eplan_task_get";
    public const string Template = "eplanmcp://tasks/{taskId}";
    private static readonly ConditionalWeakTable<McpServer, ConcurrentDictionary<RequestId, Func<Task>>> _stops = new();

    [McpServerTool(Name = ApplyTool, Title = "Apply plan", ReadOnly = false, Destructive = true,
        Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Applies a compiled plan once, using its highest step tier for policy, one approval when required, and an initial backup. Denial returns POLICY_DENIED or APPROVAL_DENIED. Dry-run rechecks and previews without consumption. Waiting expiry or disconnect leaves the task running. Inspect results with eplan_plan_get or eplan_task_get. Example: {\"plan_id\":\"0123456789abcdef0123456789abcdef\",\"wait_seconds\":0}.")]
    public static async Task<CallToolResult> ApplyAsync(PlanOrchestrator orchestrator, TaskRegistry tasks,
        RequestContext<CallToolRequestParams> context, [MinLength(1)] string plan_id, bool dry_run = false,
        [Range(0, 110)] int wait_seconds = 90, IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        McpServer server = context.Server;
        try
        {
            _ = tasks.OwnerFor(server);
            if (dry_run)
            {
                using PlanPreparation preview = await orchestrator.PrepareAsync(server, plan_id, true,
                    CallProgress.For(progress), cancellationToken).ConfigureAwait(false);
                return PlanToolResults.Plan(preview.Record, true);
            }

            var admitted = new TaskCompletionSource<TaskRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA2000 // Ownership transfers to the worker, which disposes this source after settlement.
            var worker = new CancellationTokenSource();
#pragma warning restore CA2000
            ConcurrentDictionary<RequestId, Func<Task>> stops = Stops(server);
            stops[context.JsonRpcRequest.Id] = worker.CancelAsync;
            Task<CallToolResult> completion = Task.Run(async () =>
            {
                using var ownedWorker = worker;
                try
                {
                    using PlanPreparation prepared = await orchestrator.PrepareAsync(server, plan_id, false, null,
                        worker.Token, task => admitted.TrySetResult(task)).ConfigureAwait(false);
                    PlanRecord record = prepared.Record.State.Result is not null ? prepared.Record
                        : await orchestrator.ExecuteAsync(server, prepared, null, worker.Token).ConfigureAwait(false);
                    admitted.TrySetResult(prepared.Task);
                    return PlanToolResults.Plan(record, false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return PlanToolResults.Failure(ex, true);
                }
                finally
                {
                    stops.TryRemove(context.JsonRpcRequest.Id, out _);
                    admitted.TrySetResult(null);
                }
            }, CancellationToken.None);
            TaskRecord? task = await admitted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (task is null)
            {
                return await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            TaskRecord snapshot = await tasks.WaitAsync(server, task.TaskId, wait_seconds,
                CallProgress.For(progress), cancellationToken).ConfigureAwait(false);
            stops.TryRemove(context.JsonRpcRequest.Id, out _);
            return wait_seconds != 0 && snapshot.IsTerminal
                ? await completion.WaitAsync(cancellationToken).ConfigureAwait(false)
                : ToolResults.Success("Plan admitted; use eplan_task_get to observe completion.", new { snapshot.TaskId, snapshot.Status, snapshot.PlanId });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return PlanToolResults.Failure(ex, true);
        }
    }

    // Intercept before SDK cancellation dispatch; request tokens also signal disconnect, which must not stop work.
    internal static McpMessageHandler CancelApply(McpMessageHandler next) => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcNotification { Method: NotificationMethods.CancelledNotification } notification
            && notification.Params?.Deserialize<CancelledNotificationParams>(HostJson.Options) is { } cancelled
            && Stops(context.Server).TryRemove(cancelled.RequestId, out Func<Task>? stop))
        {
            try
            {
                await stop().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Completion won the race with cancellation; the terminal result is final.
            }
        }
        await next(context, cancellationToken).ConfigureAwait(false);
    };

    private static ConcurrentDictionary<RequestId, Func<Task>> Stops(McpServer server) =>
        _stops.GetOrCreateValue(server.Services?.GetService(typeof(McpServer)) as McpServer ?? server);

    [McpServerTool(Name = TaskTool, Title = "Get task", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reads or waits for a plan task in its creating stdio session, without EPLAN access. Terminal results reference eplan_plan_get; failed tasks are successful queries. Waiting expiry or cancellation only stops this wait. Example: {\"task_id\":\"0123456789abcdef0123456789abcdef\",\"wait_seconds\":0}.")]
    public static async Task<CallToolResult> GetAsync(TaskRegistry tasks, McpServer server,
        [MinLength(1)] string task_id, [Range(0, 110)] int wait_seconds = 90,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            TaskRecord task = await tasks.WaitAsync(server, task_id, wait_seconds,
                CallProgress.For(progress), cancellationToken).ConfigureAwait(false);
            System.Text.Json.Nodes.JsonObject data = ContextBuilder.Node(task);
            if (task.Error is not null)
            {
                data["error"]!["metadata"] = PlanToolResults.Metadata(task.Error.Metadata);
            }
            return ToolResults.Success("Task state; inspect result for the plan's execution and verification.", ContextBuilder.Clean(data));
        }
        catch (TaskRegistryException ex)
        {
            return PlanToolResults.Failure(ex, false);
        }
    }

    [McpServerResource(UriTemplate = Template, Name = "plan_task", Title = "Plan task", MimeType = "application/json")]
    [Description("Task snapshot for its creating stdio session; terminal results reference the saved plan.")]
    public static async Task<string> ReadAsync(TaskRegistry tasks, McpServer server, string taskId, CancellationToken cancellationToken)
    {
        CallToolResult call = await GetAsync(tasks, server, taskId, 0, cancellationToken: cancellationToken).ConfigureAwait(false);
        return call.StructuredContent!.Value.GetRawText();
    }
}

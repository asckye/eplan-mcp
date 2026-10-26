using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Tool-local routing onto the unchanged export and write pipelines (ADR-0021).</summary>
[McpServerToolType]
internal sealed partial class ActionRunTools(IBridgeClient bridge, WritePipeline writes, ExportFolders folders, HostDataLocations locations, ActionRunCatalog catalog, HostConfig config, AuditLog audit, TimeProvider clock, ILoggerFactory logs)
{
    public const string Tool = "eplan_action_run";
    [McpServerTool(Name = Tool, Title = "Run a whitelisted EPLAN action", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(JsonObject))]
    [Description("Run a whitelisted EPLAN task; fingerprint checks only.\nPrefer task tools. Write supports dry_run; approval outside scratch; denial: APPROVAL_DENIED.\nfull + actionRun:on only.\nParameters/effects: eplan_tool_usage.\nExample: {\"entry\":\"graphicallayertable/EXPORT\",\"project\":\"Line1\",\"arguments\":{\"EXPORTFILE\":\"layers.elc\"}}")]
    public async Task<CallToolResult> RunAsync(IProgress<ProgressNotificationValue> progress, string entry, JsonElement arguments, string? project = null, bool dry_run = false, CancellationToken cancellationToken = default)
    {
        ActionRunData? data = null;
        ExportOutcome? exported = null;
        JsonArray files = [];
        long started = clock.GetTimestamp();
        bool read = false;
        bool auditAttempted = false;
        string failure = ErrorReason.Internal;
        try
        {
            if (config.ActionRun != ActionRunSwitch.On)
            {
                throw ActionRunCatalog.Denied("actionRun is off in config.json.");
            }
            (JsonObject rule, ActionRunArgs args) = catalog.Check(entry, project, arguments);
            read = ActionRunCatalog.Text(rule, "tier") == "export";
            HandshakeResult? status = (await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake;
            if (status is null)
            {
                throw new BridgeCallException(BridgeErrors.NotConnected());
            }
            if (!Version.TryParse(status.Eplan.Version, out Version? version) || version.Major != 2026)
            {
                throw ActionRunCatalog.Denied("This EPLAN major version has not been verified.");
            }
            WritePlan plan;
            string? execution = null;
            bool recovered = false;
            TimeSpan limit = TimeSpan.FromSeconds(rule["seconds"]!.GetValue<int>());
            if (!read)
            {
                var request = new WriteRequest<ActionRunArgs, ActionRunArgs>(Tool, ToolTier.Write, OperationNames.ActionRunPlan, OperationNames.ActionRunExecute, args,
                    p => new ActionRunArgs { Entry = args.Entry, Project = args.Project, Parameters = args.Parameters, Fingerprint = p.Fingerprint }, dry_run, limit, "Running whitelisted action");
                WriteOutcome<ActionRunData> outcome = await writes.RunAsync<ActionRunArgs, ActionRunArgs, ActionRunData>(request, progress, cancellationToken).ConfigureAwait(false);
                plan = outcome.Plan;
                data = outcome.Data;
                execution = outcome.Execution;
                recovered = outcome.Recovered;
            }
            else if (dry_run)
            {
                plan = await bridge.InvokeAsync<ActionRunArgs, WritePlan>(OperationNames.ActionRunPlan, args, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var adapter = new ActionExportBridge(bridge, args, locations);
                var runner = new ExportRunner(adapter, folders, clock, logs.CreateLogger<ExportRunner>());
                var job = new ExportJob(OperationNames.ActionRunPlan, OperationNames.ActionRun, new ExportArgs(), "Running whitelisted action")
                {
                    SingleFile = ActionRunCatalog.Extension(rule, args),
                    Limit = limit,
                    Folder = ActionRunCatalog.Text(rule, "scope") == "project" ? null : args.Parameters.Any(p => p.Name == "MDTYPE" && p.Value == "LANGUAGES") ? "translation-database" : "parts-database",
                };
                exported = await runner.RunAsync(job, progress, cancellationToken).ConfigureAwait(false);
                plan = adapter.Plan!;
                data = adapter.Data!;
                files = adapter.Files;
            }
            data ??= JsonSerializer.Deserialize<ActionRunData>(plan.Details!["actionRun"], HostJson.Options)!;
            bool changed = read && data.After is not null && !data.Before.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(data.After.OrderBy(p => p.Key, StringComparer.Ordinal));
            if (changed)
            {
                catalog.Disabled.TryAdd(entry, 0);
                LogDisabled(logs.CreateLogger<ActionRunTools>(), entry);
            }
            JsonObject result = Shape(rule, args, plan, data, exported, files, dry_run, recovered, execution, changed, locations);
            if (read)
            {
                auditAttempted = true;
                await audit.AppendAsync(new JsonObject { ["type"] = "action_run", ["tool"] = Tool, ["entry"] = entry, ["elapsedMs"] = clock.GetElapsedTime(started).TotalMilliseconds, ["result"] = result.DeepClone() }, CancellationToken.None).ConfigureAwait(false);
            }
            return ToolResults.Success(dry_run ? "Whitelisted action planned; nothing executed." : read ? "Whitelisted action exported one file; see 'directory'." : "Whitelisted action committed; fingerprint readback only.", result);
        }
        catch (AuditUnavailableException)
        {
            if (exported is not null)
            {
                new ExportFolder(locations.Exports, exported.Directory).TryDelete();
            }
            return ToolResults.Error(new BridgeError { Reason = ErrorReason.AuditUnavailable, Message = "The action audit could not be written; ask the engineer to check the audit directory." });
        }
        catch (BridgeCallException ex) { failure = ex.Error.Reason; if (entry == "changelayer") { ex.Error.Execution ??= ExecutionState.NotStarted; } return ToolResults.Error(ex.Error); }
        catch (OperationCanceledException) { failure = ErrorReason.Cancelled; throw; }
        finally
        {
            if (read && !auditAttempted)
            {
                try
                {
                    await audit.AppendAsync(new JsonObject { ["type"] = "action_run", ["tool"] = Tool, ["entry"] = entry, ["elapsedMs"] = clock.GetElapsedTime(started).TotalMilliseconds, ["reason"] = failure }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (AuditUnavailableException) { LogAuditFailure(logs.CreateLogger<ActionRunTools>()); }
            }
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Action entry {Entry} changed its fingerprint and is disabled until this Host restarts; reverify it before reuse")]
    private static partial void LogDisabled(ILogger logger, string entry);
    [LoggerMessage(Level = LogLevel.Error, Message = "The final action export failure/cancellation audit could not be written; inspect the audit directory")]
    private static partial void LogAuditFailure(ILogger logger);
    private static JsonObject Shape(JsonObject rule, ActionRunArgs args, WritePlan plan, ActionRunData data, ExportOutcome? export, JsonArray files, bool dry, bool recovered, string? execution, bool disabled, HostDataLocations locations)
    {
        bool truncated = data.Before.Count(p => p.Key.StartsWith("file:", StringComparison.Ordinal)) > 20 || data.After?.Count(p => p.Key.StartsWith("file:", StringComparison.Ordinal)) > 20 || data.EplanMessageCount > 20 || files.Count > 50;
        var result = new JsonObject
        {
            ["entry"] = args.Entry,
            ["tier"] = ActionRunCatalog.Text(rule, "tier"),
            ["project"] = plan.Project.Length == 0 ? null : plan.Project,
            ["target"] = plan.Project.Length > 0 ? plan.Project : Path.GetFileName(data.Target),
            ["action"] = ActionRunCatalog.Text(rule, "action"),
            ["type"] = rule["fixed"]?["TYPE"]?.DeepClone(),
            ["parameters"] = new JsonArray([.. data.Parameters.Select(p => (JsonNode)new JsonObject { ["name"] = p.Name, ["value"] = Display(p, plan.Project, locations) })]),
            ["dryRun"] = dry,
            ["before"] = Fingerprint(data.Before),
            ["after"] = data.After is null ? null : Fingerprint(data.After),
            ["readback"] = data.After is null ? "not_executed" : "fingerprint",
            ["directory"] = export?.Directory,
            ["files"] = new JsonArray([.. files.Take(50).Select(f => f!.DeepClone())]),
            ["fileCount"] = files.Count,
            ["totalBytes"] = export?.Files.Sum(f => f.Bytes) ?? 0,
            ["entryDisabled"] = disabled,
            ["eplanMessages"] = JsonSerializer.SerializeToNode(data.EplanMessages.Select(m => ResultShaper.CleanText(m))),
            ["truncated"] = truncated,
            ["hint"] = "Changes have not been checked item by item; use read tools to verify objects you care about. " + ActionRunCatalog.Text(rule, "effects") + " " + ResultShaper.CleanText(data.Warning)
                + (data.After is not null && data.Before.TryGetValue("layer:visible", out string? visible) ? $" Layer visibility: {visible} -> {data.After.GetValueOrDefault("layer:visible")}." : string.Empty)
                + (recovered ? " Execution was recovered; unreadable post-action state is not inferred." : string.Empty) + (disabled ? " Entry disabled; ask the engineer to reverify it." : string.Empty),
        };
        if (execution is not null)
        {
            result["execution"] = execution;
            result["recovered"] = recovered;
            result["undo"] = null;
        }
        return result;
    }
    private static string Display(ActionParameter p, string project, HostDataLocations locations) => p.Name is "PROJECTNAME" or "Project" ? project
        : p.Name == "SOURCEPATH" ? "." : p.Name is "EXPORTFILE" or "XMLFile" or "FILENAME" or "DESTINATIONFILE" or "DESTINATIONPATH" or "DESTINATION" && Path.IsPathFullyQualified(p.Value) ? Path.GetRelativePath(locations.Root, p.Value) : ResultShaper.CleanText(p.Value) ?? string.Empty;
    private static JsonObject Fingerprint(Dictionary<string, string> state)
    {
        var files = state.Where(p => p.Key.StartsWith("file:", StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        return new JsonObject
        {
            ["counts"] = new JsonArray([.. state.Where(p => p.Key.StartsWith("count:", StringComparison.Ordinal)).Select(p => (JsonNode)new JsonObject { ["kind"] = p.Key[6..], ["count"] = p.Value == "null" ? null : int.Parse(p.Value, CultureInfo.InvariantCulture) })]),
            ["dataFiles"] = new JsonArray([.. files.Take(20).Select(p => (JsonNode)new JsonObject { ["path"] = p.Key[5..], ["bytes"] = long.Parse(p.Value.Split('\t')[0], CultureInfo.InvariantCulture), ["modifiedUtc"] = p.Value.Split('\t')[1] })]),
            ["fileCount"] = files.Length,
            ["truncated"] = files.Length > 20,
        };
    }
}

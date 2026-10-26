using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PropertySetTools
{
    public const string Tool = "eplan_property_set";

    [McpServerTool(Name = Tool, Title = "Set EPLAN properties", ReadOnly = false,
        Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Sets verified Function properties. One Ctrl+Z undoes a call. Outside scratch needs approval; denial: APPROVAL_DENIED. Example: {\"project\":\"Line1\",\"changes\":[{\"target\":{\"device_tag\":\"=AP+ST1-K1\"},\"property\":{\"property_id\":20027},\"value\":\"DC 24V\"}],\"dry_run\":true}.")]
    public static async Task<CallToolResult> SetAsync(IBridgeClient bridge, WritePipeline pipeline,
        RequestContext<CallToolRequestParams> context, IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            PropertySetArgs args = PropertySetRequest.Parse(context.Params?.Arguments);
            bool dryRun = context.Params!.Arguments!.TryGetValue("dry_run", out JsonElement dry) && dry.GetBoolean();
            bool detailed = context.Params.Arguments.TryGetValue("response_format", out JsonElement format) && format.GetString() == "detailed";
            BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.Problem is { } problem)
            {
                throw new BridgeCallException(problem);
            }
            if (status.Handshake?.Eplan.Version.Split('.')[0] != "2026")
            {
                throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PolicyDenied, Message = "B1 property writes require verified EPLAN 2026." });
            }
            ProjectGetData project = await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet,
                new() { Project = args.Project }, cancellationToken).ConfigureAwait(false);
            args.Project = project.Project.Path;
            args.Approved = await bridge.InvokeAsync<PropertySetArgs, PropertySetPlan>(OperationNames.PropertySetPlan,
                args, TimeSpan.FromSeconds(30), null, cancellationToken).ConfigureAwait(false);
            var rows = new JsonArray();
            foreach (PropertySetGuard guard in args.Approved.Guards)
            {
                foreach (SnapshotText after in guard.After.Where(t => t.Text != guard.Before.FirstOrDefault(b => b.Language == t.Language)?.Text
                    && (t.Text.Length > 0 || guard.Before.Any(b => b.Language == t.Language && b.Text.Length > 0))))
                {
                    ObjectLocator target = guard.Target;
                    var row = JsonSerializer.SerializeToNode(new
                    {
                        target = new { target.Kind, target.Type, target.Oid, target.Name, target.Page, target.Category, target.Pos },
                        property_id = guard.PropertyId,
                        index = (int?)null,
                        language = guard.PropertyId == 20011 ? after.Language : null,
                        before = guard.Before.FirstOrDefault(b => b.Language == after.Language)?.Text ?? string.Empty,
                        after = after.Text,
                    }, HostJson.Options)!.AsObject();
                    if (detailed)
                    {
                        row["side_effects"] = JsonSerializer.SerializeToNode(PropertyWriteWhitelist.PlanningCandidate(2026, "Function", guard.PropertyId)!.SideEffectGuards);
                    }
                    rows.Add(row);
                }
            }
            JsonObject result = JsonSerializer.SerializeToNode(new
            {
                project = args.Approved.Plan.Project,
                changes = rows,
                changed_count = rows.Count,
                execution = ExecutionState.NotStarted,
                undo = (string?)null,
                warnings = Array.Empty<string>(),
                dry_run = dryRun,
                recovered = false,
            }, HostJson.Options)!.AsObject();
            result = ContextBuilder.Clean(result).AsObject();
            int budget = detailed ? ResultShaper.MaxTokenBudget : ResultShaper.DefaultTokenBudget;
            if (ResultShaper.ResultTokens(result.ToJsonString()) + 2048 > budget)
            {
                throw new BridgeCallException(ToolResults.InvalidArgumentError(["The complete property preview exceeds the budget; reduce changes or use detailed."]));
            }
            JsonArray keys = new([.. rows.Select(r => (JsonNode)new JsonObject
            {
                ["target"] = r!["target"]!.DeepClone(),
                ["property_id"] = r["property_id"]!.DeepClone(),
                ["index"] = null,
                ["language"] = r["language"]?.DeepClone(),
            })]);
            WritePlan plan = JsonSerializer.Deserialize<WritePlan>(JsonSerializer.Serialize(args.Approved.Plan, HostJson.Options), HostJson.Options)!;
            plan.Changes = [.. rows.Select(r => new PlannedChange
            {
                Action = "update",
                Kind = "function",
                Name = $"{r!["target"]!["name"]} (page {r["target"]!["page"]}; type {r["target"]!["type"]}; oid {r["target"]!["oid"]})",
                Property = $"{r["property_id"]} ({r["language"]?.GetValue<string>() ?? "??_??"})",
                OldValue = r["before"]!.GetValue<string>(),
                NewValue = r["after"]!.GetValue<string>(),
            })];
            keys = ContextBuilder.Clean(keys).AsArray();
            plan.Details = new() { ["property_keys"] = keys.ToJsonString(HostJson.Options) };
            var request = new WriteRequest<PropertySetArgs, PropertySetArgs>(Tool, ToolTier.Write, OperationNames.PropertySetPlan,
                OperationNames.PropertySetExecute, args, _ => args, dryRun, TimeSpan.FromSeconds(30), "Setting EPLAN properties")
            {
                PreparedPlan = plan,
                AuditArguments = new { argumentsHash = WritePipeline.HashArguments(args.Changes), properties = keys },
            };
            WriteOutcome<PropertySetData> outcome = await pipeline.RunAsync<PropertySetArgs, PropertySetArgs, PropertySetData>(request, progress, cancellationToken).ConfigureAwait(false);
            result["execution"] = outcome.Execution;
            result["undo"] = outcome.Execution == ExecutionState.Committed ? "AI: " + Tool : null;
            result["recovered"] = outcome.Recovered;
            result["warnings"] = new JsonArray([.. new[] { outcome.Data?.Warning,
                outcome.Recovered ? "EPLAN confirmed the commit after its response was lost; changes show the original plan." : null }
                .OfType<string>().Select(w => (JsonNode?)JsonValue.Create(ResultShaper.CleanText(w)))]);
            string summary = outcome.Execution == ExecutionState.Committed
                ? string.Create(CultureInfo.InvariantCulture, $"Changed {rows.Count} properties; Ctrl+Z in EPLAN undoes this step.")
                : string.Create(CultureInfo.InvariantCulture, $"Planned {rows.Count} property changes.");
            return ToolResults.Success(summary, result);
        }
        catch (BridgeCallException ex)
        {
            ex.Error.Execution ??= ExecutionState.NotStarted;
            return ToolResults.Error(ex.Error);
        }
    }
}

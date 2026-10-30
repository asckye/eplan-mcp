using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
internal sealed class ConnectionCreateTools
{
    public const string Tool = "eplan_connection_create";
    [McpServerTool(Name = Tool, Title = "Create connection symbols and explicit wire paths", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates SPECIAL nodes, interruption points, wires and cable definitions on a Circuit page; verifies endpoints and explicit core labels/colours. Wires: 2 undo steps; cables: 6-8, shown in preview. Failure undoes this call. Outside scratch needs approval; denial returns APPROVAL_DENIED. See usage. Example: {\"project\":\"Line1\",\"page\":\"=F+A1&EFS1/1\",\"items\":[{\"kind\":\"corner\",\"position\":[24,16],\"directions\":[\"Left\",\"Up\"]}],\"dry_run\":true}.")]
    public static async Task<CallToolResult> CreateAsync(WritePipeline pipeline, IBridgeClient bridge,
        [Description("Project name/path")] string project, string page,
        [Description("1-50 wire/corner/t_node/potential_point/interruption_point/cable_definition; interruptions require counterpart_pages; see usage")] JsonElement[] items,
        bool dry_run = false, bool snap_to_grid = true, [AllowedValues("concise", "detailed")] string response_format = "concise",
        [Description(PageDisplay.Description)] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        var args = new { project, page, items, dry_run, snap_to_grid, response_format };
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, project, page, cancellationToken).ConfigureAwait(false);
        try
        {
            var request = new WriteRequest<object, object>(Tool, ToolTier.Write, OperationNames.ConnectionCreatePlan, OperationNames.ConnectionCreateExecute,
                args, plan => new { request = args, fingerprint = plan.Fingerprint }, dry_run, PageWriteTools.DefaultLimit, "Creating connections");
            WriteOutcome<JsonObject> outcome = await pipeline.RunAsync<object, object, JsonObject>(request, progress, cancellationToken).ConfigureAwait(false);
            JsonObject result = outcome.Data ?? JsonNode.Parse(outcome.Plan.Details!["preview"])!.AsObject();
            result["execution"] = outcome.Execution;
            result["dryRun"] = dry_run;
            result["recovered"] = outcome.Recovered;
            void Clean(JsonNode node)
            {
                foreach (JsonNode child in node is JsonObject obj ? obj.Select(p => p.Value).OfType<JsonNode>().ToArray() : node is JsonArray arr ? arr.OfType<JsonNode>().ToArray() : [])
                { if (child is JsonValue v && v.TryGetValue(out string? text)) { child.ReplaceWith(ResultShaper.CleanText(text)); } else { Clean(child); } }
            }
            Clean(result);
            // Acceptance trial: the scope carries every placement of the scoped pages (up to 10000) as planning evidence and
            // pushed the result over budget; report its size, the pairing outcome is in interruptionPoints.
            if (result["interruptionScope"] is JsonObject scope)
            {
                result["interruptionScope"] = new JsonObject
                {
                    ["pages"] = scope["pages"] is JsonArray scoped ? new JsonArray(scoped.Select(p => (JsonNode?)JsonValue.Create(p?["page"]?.GetValue<string>())).ToArray()) : null,
                    ["placements"] = scope["pages"] is JsonArray counted ? counted.Sum(p => p?["drawing"]?["placements"] is JsonArray a ? a.Count : 0) : 0,
                    ["points"] = scope["points"] is JsonArray points ? points.Count : 0,
                    ["preservedEndpoints"] = scope["preservedEndpoints"] is JsonArray kept ? kept.Count : 0,
                };
            }
            int budget = response_format == "detailed" ? ResultShaper.MaxTokenBudget : ResultShaper.DefaultTokenBudget;
            foreach (string field in new[] { "interruptionScope", "coordinates", "symbols", "graphicalAutoconnect", "logicalAfterGenerate", "actualEndpoints", "interruptionPoints", "cableDefinitions", "coreImpact", "actualCores" })
            {
                if (ResultShaper.EstimateTokens(result.ToJsonString()) > budget)
                { result.Remove(field); result["truncated"] = true; }
            }
            return display.Apply(ToolResults.Success(dry_run ? "Dry run: connection and core impact predicted. Nothing was changed." : $"Connections created; {result["undoSteps"]} undo steps (Ctrl+Z).", result));
        }
        catch (BridgeCallException ex) { return display.Apply(ToolResults.Error(ex.Error)); }
    }
}

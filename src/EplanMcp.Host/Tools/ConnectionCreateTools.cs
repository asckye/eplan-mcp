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
    [Description("Creates SPECIAL nodes and orthogonal wires, then verifies generated endpoints. 2 undo steps; failure undoes placement. Outside scratch needs approval. See usage for fields. Example: {\"project\":\"Line1\",\"page\":\"=F+A1&EFS1/1\",\"items\":[{\"kind\":\"corner\",\"position\":[24,16],\"directions\":[\"Left\",\"Up\"]}],\"dry_run\":true}.")]
    public static async Task<CallToolResult> CreateAsync(WritePipeline pipeline,
        [Description("Project name/path")] string project, [Description("Exact Circuit page")] string page,
        [Description("1-50 wire/corner/t_node/potential_point; see usage")] JsonElement[] items,
        bool dry_run = false, bool snap_to_grid = true, [AllowedValues("concise", "detailed")] string response_format = "concise",
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        var args = new { project, page, items, dry_run, snap_to_grid, response_format };
        try
        {
            var request = new WriteRequest<object, object>(Tool, ToolTier.Write, OperationNames.ConnectionCreatePlan, OperationNames.ConnectionCreateExecute,
                args, plan => new { request = args, fingerprint = plan.Fingerprint }, dry_run, PageWriteTools.DefaultLimit, "Creating connections (2 undo steps)");
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
            int budget = response_format == "detailed" ? ResultShaper.MaxTokenBudget : ResultShaper.DefaultTokenBudget;
            foreach (string field in new[] { "coordinates", "symbols", "graphicalAutoconnect", "logicalAfterGenerate", "actualEndpoints" })
            {
                if (ResultShaper.EstimateTokens(result.ToJsonString()) > budget)
                { result.Remove(field); result["truncated"] = true; }
            }
            return ToolResults.Success(dry_run ? "Dry run: connection geometry predicted; 2 undo steps on execution. Nothing was changed." : "Connections created; 2 undo steps (Ctrl+Z twice).", result);
        }
        catch (BridgeCallException ex) { return ToolResults.Error(ex.Error); }
    }
}

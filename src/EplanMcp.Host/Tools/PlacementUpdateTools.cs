using System.ComponentModel;
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
internal sealed class PlacementUpdateTools
{
    public const string Tool = "eplan_placement_update";
    [McpServerTool(Name = Tool, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<CallToolResult> UpdateAsync(WritePipeline pipeline, IBridgeClient bridge, RequestContext<CallToolRequestParams> context,
        [Description(PageDisplay.Description)] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        JsonElement input = JsonSerializer.SerializeToElement((context.Params?.Arguments ?? new Dictionary<string, JsonElement>()).Where(p => p.Key != "show_page").ToDictionary());
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, input, cancellationToken).ConfigureAwait(false);
        bool dryRun = input.TryGetProperty("dry_run", out JsonElement dry) && dry.ValueKind == JsonValueKind.True;
        try
        {
            var request = new WriteRequest<JsonElement, object>(Tool, ToolTier.Write, OperationNames.ConnectionCreatePlan, OperationNames.ConnectionCreateExecute,
                input, plan => new { request = input, fingerprint = plan.Fingerprint }, dryRun, PageWriteTools.DefaultLimit, "Moving placements");
            WriteOutcome<JsonObject> outcome = await pipeline.RunAsync<JsonElement, object, JsonObject>(request, progress, cancellationToken).ConfigureAwait(false);
            JsonObject result = outcome.Data ?? JsonNode.Parse(outcome.Plan.Details!["preview"])!.AsObject();
            result["execution"] = outcome.Execution;
            result["dryRun"] = dryRun;
            result["recovered"] = outcome.Recovered;
            return display.Apply(ToolResults.Success(outcome.Execution == ExecutionState.Committed ? "Moved placements; one Ctrl+Z restores the closure." : "Nothing was changed.", ContextBuilder.Clean(result)));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return display.Apply(ToolResults.Error(ex.Error)); }
    }
}

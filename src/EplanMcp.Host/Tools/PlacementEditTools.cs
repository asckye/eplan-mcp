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
internal sealed class PlacementEditTools
{
    internal const string Tool = "eplan_placement_edit";
    [McpServerTool(Name = Tool, Title = "Edit placements on one EPLAN page", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<CallToolResult> EditAsync(WritePipeline pipeline, RequestContext<CallToolRequestParams> context, IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        JsonElement input = JsonSerializer.SerializeToElement(context.Params?.Arguments ?? new Dictionary<string, JsonElement>());
        bool dry = input.TryGetProperty("dry_run", out JsonElement value) && value.ValueKind == JsonValueKind.True;
        ToolTier tier = input.TryGetProperty("operation", out value) && value.ValueKind == JsonValueKind.String && value.GetString() == "delete" ? ToolTier.Destructive : ToolTier.Write;
        try
        {
            var request = new WriteRequest<JsonElement, object>(Tool, tier, OperationNames.PlacementEditPlan, OperationNames.PlacementEditExecute, input, (plan, backup) => new { request = input, fingerprint = plan.Fingerprint, backup }, dry, PageWriteTools.DefaultLimit, "Editing placements");
            WriteOutcome<JsonObject> outcome = await pipeline.RunAsync<JsonElement, object, JsonObject>(request, progress, cancellationToken).ConfigureAwait(false);
            JsonObject result = outcome.Data ?? JsonNode.Parse(outcome.Plan.Details!["preview"])!.AsObject();
            result["execution"] = outcome.Execution;
            result["dryRun"] = dry;
            result["recovered"] = outcome.Recovered;
            result["backup"] = outcome.Backup;
            if (ResultShaper.ResultTokens(result.ToJsonString()) > (input.TryGetProperty("response_format", out value) && value.GetString() == "detailed" ? 20000 : 5000))
            { result.Remove("before"); result.Remove("after"); }
            return ToolResults.Success(outcome.Recovered ? "Committed outcome recovered without readback data; use eplan_object_find for fresh locators. Do not replay the edit." : outcome.Execution == ExecutionState.Committed ? "Placements edited; one Ctrl+Z restores the edit. Regenerate connections separately if needed." : "Nothing was changed.", ContextBuilder.Clean(result));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error); }
    }
    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        ModelContextProtocol.Protocol.Tool tool = tools.Single(t => t.ProtocolTool.Name == Tool).ProtocolTool;
        tool.Description = "Deletes, copies, rotates, mirrors, aligns or distributes eplan_object_find locators on one Circuit page of an open project. mm, bottom left, y up. Rotation: 90-degree steps, positive counterclockwise. Copy preserves tags and may associate devices. Delete always needs approval and backup; other edits need approval outside scratch. Denial: APPROVAL_DENIED. Preview with dry_run; one undo. See eplan_tool_usage.";
        JsonObject schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","operation","locators"],"properties":{"project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},"operation":{"enum":["delete","copy","rotate","mirror","align","distribute"]},"locators":{"type":"array","minItems":1,"maxItems":50,"items":{"$ref":"#/$defs/locator"}},"offset_mm":{"$ref":"#/$defs/point"},"target_mm":{"$ref":"#/$defs/point"},"step_mm":{"$ref":"#/$defs/point"},"pivot_mm":{"$ref":"#/$defs/point"},"copies":{"type":"integer","minimum":1,"maximum":20,"default":1},"quarter_turns":{"enum":[-3,-2,-1,1,2,3]},"axis":{"enum":["horizontal","vertical"]},"coordinate_mm":{"type":"number","minimum":-10000,"maximum":10000},"alignment":{"enum":["left","right","top","bottom","centre_x","centre_y"]},"dry_run":{"type":"boolean","default":false},"response_format":{"enum":["concise","detailed"],"default":"concise"}}}""")!.AsObject();
        schema["$defs"] = JsonNode.Parse(PlacementUpdateContract.Input.GetProperty("$defs").GetRawText());
        schema["$defs"]!["point"] = JsonNode.Parse("""{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number","minimum":-10000,"maximum":10000}}""");
        tool.InputSchema = JsonSerializer.SerializeToElement(schema);
        tool.OutputSchema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","operation","execution","dryRun","fingerprint","predicted","removedConnections","newLocators","noOp","recovered","backup"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"operation":{"enum":["delete","copy","rotate","mirror","align","distribute"]},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"fingerprint":{"type":"string"},"predicted":{"type":"array"},"removedConnections":{"type":"array"},"newLocators":{"type":"array"},"noOp":{"type":"boolean"},"recovered":{"type":"boolean"},"backup":{"type":["string","null"]},"warning":{"type":["string","null"]},"before":{"type":"object"},"after":{"type":"object"}}}""").RootElement.Clone();
    }
}

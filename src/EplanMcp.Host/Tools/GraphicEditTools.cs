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
internal sealed class GraphicEditTools
{
    internal const string Tool = "eplan_graphic_edit";

    [McpServerTool(Name = Tool, Title = "Edit page graphics", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<CallToolResult> EditAsync(WritePipeline pipeline, RequestContext<CallToolRequestParams> context,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        JsonElement input = JsonSerializer.SerializeToElement(context.Params?.Arguments ?? new Dictionary<string, JsonElement>());
        bool dry = WriteToolDefaults.IsPreview(input);
        try
        {
            var request = new WriteRequest<JsonElement, object>(Tool, ToolTier.Write, OperationNames.GraphicEditPlan, OperationNames.GraphicEditExecute,
                input, (plan, _) => new { request = input, fingerprint = plan.Fingerprint }, dry, PageWriteTools.DefaultLimit, "Editing page graphics");
            WriteOutcome<JsonObject> outcome = await pipeline.RunAsync<JsonElement, object, JsonObject>(request, progress, cancellationToken).ConfigureAwait(false);
            JsonObject result = outcome.Data ?? JsonNode.Parse(outcome.Plan.Details!["preview"])!.AsObject();
            result["execution"] = outcome.Execution;
            result["dryRun"] = dry;
            result["recovered"] = outcome.Recovered;
            if (dry)
            { result["hint"] = WriteToolDefaults.ExecutionHint; }
            if (ResultShaper.ResultTokens(result.ToJsonString()) > (input.TryGetProperty("response_format", out JsonElement format) && format.GetString() == "detailed" ? 20000 : 5000))
            { result.Remove("before"); result.Remove("after"); }
            return ToolResults.Success(outcome.Recovered ? "Committed outcome recovered without readback data; refresh eplan_object_find locators. Do not replay the edit."
                : outcome.Execution == ExecutionState.Committed ? "Page graphics edited; one Ctrl+Z restores the edit." : "Nothing was changed.", ContextBuilder.Clean(result));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error); }
    }

    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        ModelContextProtocol.Protocol.Tool tool = tools.Single(t => t.ProtocolTool.Name == Tool).ProtocolTool;
        tool.Description = "Edits page graphics using same-page eplan_object_find locators: scale, drawing order, groups, blocks, free-text docking, rectangle corners, format, trimming, length, stretch, grid, chamfer and fillet. Coordinates: mm, lower left, y up. Scale requires origin_mm. Omit dry_run to preview; false executes. One undo; approval outside scratch. See eplan_tool_usage for operation fields and stretch rules.";
        tool.InputSchema = JsonDocument.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","page","operation","locators"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"operation":{"enum":["scale","order","group","ungroup","explode_block","dock_text","undock_text","corner_radius","copy_format","cut_off","set_length","stretch","snap_to_grid","chamfer","fillet"]},"locators":{},"scale_x":{},"scale_y":{},"origin_mm":{},"to":{},"drawing_order":{},"dock_direction":{},"radius_mm":{},"source":{},"point_mm":{},"length_mm":{},"anchor":{},"region_mm":{},"offset_mm":{},"distance_mm":{},"layout_guard":{"enum":["refuse","warn"],"default":"refuse"},"dry_run":{"type":"boolean","default":true},"response_format":{"enum":["concise","detailed"],"default":"concise"}}}
            """).RootElement.Clone();
        tool.OutputSchema = JsonDocument.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","page","operation","fingerprint","predicted","newLocators","noOp","execution","dryRun","recovered"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"operation":{"type":"string"},"fingerprint":{"type":"string"},"predicted":{"type":"array"},"newLocators":{"type":"array"},"noOp":{"type":"boolean"},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"recovered":{"type":"boolean"},"warning":{"type":["string","null"]},"hint":{"type":["string","null"]},"before":{"type":"object"},"after":{"type":"object"}}}
            """).RootElement.Clone();
    }
}

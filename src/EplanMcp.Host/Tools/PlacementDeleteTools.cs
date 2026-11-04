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
internal sealed class PlacementDeleteTools
{
    internal const string Tool = "eplan_placement_delete";
    [McpServerTool(Name = Tool, Title = "Delete independent drawing placements", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<CallToolResult> DeleteAsync(WritePipeline pipeline, RequestContext<CallToolRequestParams> context,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        JsonElement input = JsonSerializer.SerializeToElement(context.Params?.Arguments ?? new Dictionary<string, JsonElement>());
        var errors = new List<string>();
        foreach (string name in new[] { "project", "page" })
        { if (!input.TryGetProperty(name, out JsonElement text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString())) { errors.Add("/" + name + " requires a nonempty string."); } }
        if (input.TryGetProperty("dry_run", out JsonElement flag) && flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { errors.Add("/dry_run must be boolean."); }
        if (!input.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 100) { errors.Add("/items requires 1..100 drawing locators."); }
        else
        { foreach (JsonElement item in items.EnumerateArray()) { if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out int t) || t < 1 || !item.TryGetProperty("oid", out JsonElement oid) || oid.ValueKind != JsonValueKind.Number || !oid.TryGetInt64(out long id) || id < 1) { errors.Add("/items requires integer type/oid drawing locators."); } } }
        if (errors.Count > 0) { BridgeError error = ToolResults.InvalidArgumentError(errors); error.Execution = ExecutionState.NotStarted; return ToolResults.Error(error); }
        bool dry = WriteToolDefaults.IsPreview(input);
        try
        {
            var request = new WriteRequest<JsonElement, object>(Tool, ToolTier.Destructive, OperationNames.PlacementDeletePlan, OperationNames.PlacementDeleteExecute,
                input, (plan, backup) => new { request = input, fingerprint = plan.Fingerprint, backup }, dry, PageWriteTools.DefaultLimit, "Deleting drawing placements");
            WriteOutcome<JsonObject> outcome = await pipeline.RunAsync<JsonElement, object, JsonObject>(request, progress, cancellationToken).ConfigureAwait(false);
            JsonObject result = outcome.Data ?? JsonNode.Parse(outcome.Plan.Details!["preview"])!.AsObject();
            result["execution"] = outcome.Execution; result["dryRun"] = dry; result["recovered"] = outcome.Recovered; result["backup"] = outcome.Backup;
            if (dry) { result["hint"] = WriteToolDefaults.ExecutionHint; }
            return ToolResults.Success(dry ? "Previewed drawing deletion. Nothing was changed." : "Drawing placements deleted; one Ctrl+Z restores them.", ContextBuilder.Clean(result));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error); }
    }
    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        ModelContextProtocol.Protocol.Tool tool = tools.Single(t => t.ProtocolTool.Name == Tool).ProtocolTool;
        tool.Description = "Deletes 1-100 independent graphics by eplan_page_get drawing locators. Refuses devices, connections, property text and block members. dry_run defaults true; false needs approval and backup. One undo. See eplan_tool_usage.";
        JsonNode schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","items"],"properties":{"project":{"type":"string","minLength":1},"page":{"type":"string","minLength":1},"items":{"type":"array","minItems":1,"maxItems":100,"items":{"$ref":"#/$defs/locator"}},"dry_run":{"type":"boolean","default":true}}}""")!;
        schema["$defs"] = JsonNode.Parse(PlacementUpdateContract.Input.GetProperty("$defs").GetRawText());
        tool.InputSchema = JsonSerializer.SerializeToElement(schema);
        tool.OutputSchema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","items","fingerprint","execution","dryRun","recovered","backup"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["type","oid"],"properties":{"type":{"type":"integer"},"oid":{"type":"integer"}}}},"fingerprint":{"type":"string"},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"recovered":{"type":"boolean"},"backup":{"type":["string","null"]},"warning":{"type":["string","null"]},"hint":{"type":["string","null"]}}}""").RootElement.Clone();
    }
}

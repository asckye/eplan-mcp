using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace EplanMcp.Host.Tools;

/// <summary>eplan_potential_update (docs/tools/eplan_potential_update.md): properties of placed potential definitions.</summary>
[McpServerToolType]
internal sealed class PotentialUpdateTools
{
    internal const string Tool = "eplan_potential_update";

    [McpServerTool(Name = Tool, Title = "Update potential definitions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<CallToolResult> UpdateAsync(WritePipeline pipeline, RequestContext<CallToolRequestParams> context,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        JsonElement input = JsonSerializer.SerializeToElement(context.Params?.Arguments ?? new Dictionary<string, JsonElement>());
        var errors = new List<string>();
        foreach (string name in new[] { "project", "page" })
        { if (!input.TryGetProperty(name, out JsonElement text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString())) { errors.Add("/" + name + " requires a nonempty string."); } }
        if (input.TryGetProperty("dry_run", out JsonElement flag) && flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        { errors.Add("/dry_run must be boolean."); }
        if (!input.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 50)
        { errors.Add("/items requires 1..50 entries {locator, ...properties}."); }
        if (errors.Count > 0)
        { BridgeError error = ToolResults.InvalidArgumentError(errors); error.Execution = ExecutionState.NotStarted; return ToolResults.Error(error); }
        bool dry = WriteToolDefaults.IsPreview(input);
        try
        {
            var request = new WriteRequest<JsonElement, object>(Tool, ToolTier.Write, OperationNames.PotentialUpdatePlan, OperationNames.PotentialUpdateExecute,
                input, (plan, _) => new { request = input, fingerprint = plan.Fingerprint, projectPath = plan.ProjectPath }, dry, PageWriteTools.DefaultLimit, "Updating potential definitions");
            WriteOutcome<JsonObject> outcome = await pipeline.RunAsync<JsonElement, object, JsonObject>(request, progress, cancellationToken).ConfigureAwait(false);
            JsonObject result = outcome.Data ?? JsonNode.Parse(outcome.Plan.Details!["preview"])!.AsObject();
            result["execution"] = outcome.Execution;
            result["dryRun"] = dry;
            result["recovered"] = outcome.Recovered;
            if (dry)
            { result["hint"] = WriteToolDefaults.ExecutionHint; }
            return ToolResults.Success(dry ? "Previewed the potential definition changes. Nothing was changed." : "Potential definitions updated; one Ctrl+Z restores them.", ContextBuilder.Clean(result));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error); }
    }

    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        ModelContextProtocol.Protocol.Tool tool = tools.Single(t => t.ProtocolTool.Name == Tool).ProtocolTool;
        tool.Description = "Changes name, signal, colour (31004), cross-section (31002), value or type of 1-50 placed potential definitions or points by locator. dry_run defaults true. One undo. Then eplan_connection_generate rebuild:true. See eplan_tool_usage.";
        JsonNode schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","items"],"properties":{"project":{"description":"Open project","type":"string","minLength":1},"page":{"description":"Full page name","type":"string","minLength":1},"items":{"description":"1-50 locators with at least one property change each.","type":"array","minItems":1,"maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["locator"],"properties":{"locator":{"description":"Potential definition locator from eplan_object_find.","type":"object","required":["project","page","type","oid","digest"]},"potential_name":{"description":"New potential name, 1-200 characters.","type":"string"},"signal_name":{"description":"New signal name, 1-200 characters.","type":"string"},"color":{"description":"EPLAN colour code, e.g. BU (property 31004).","type":"string"},"cross_section":{"description":"Conductor cross-section text, e.g. 1.5 (property 31002).","type":"string"},"potential_value":{"description":"Potential value text, e.g. 230V (property 33003).","type":"string"},"potential_type":{"description":"EPLAN potential type code (property 31006).","type":"integer"}}}},"dry_run":{"description":"Omit/true previews; false executes.","type":"boolean","default":true}}}""")!;
        tool.InputSchema = JsonSerializer.SerializeToElement(schema);
        tool.OutputSchema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","items","fingerprint","execution","dryRun","recovered"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["locator","before","after"],"properties":{"locator":{"type":"object"},"before":{"type":"object"},"after":{"type":"object"}}}},"fingerprint":{"type":"string"},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"recovered":{"type":"boolean"},"warning":{"type":["string","null"]},"hint":{"type":["string","null"]}}}""").RootElement.Clone();
    }
}

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class ConnectionCreateTools
{
    public const string Tool = "eplan_connection_create";
    /// <summary>
    /// Live 2026-11-02 (d3 acceptance): a cable batch commits six times with full-page readbacks and took 66.5 s on a
    /// dense page, past the 60 s write limit, which left its outcome unknown.
    /// </summary>
    internal static readonly TimeSpan CableLimit = TimeSpan.FromSeconds(180);
    internal static TimeSpan Limit(JsonElement[] items) => items.Any(i => i.ValueKind == JsonValueKind.Object
        && i.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "cable_definition")
        ? CableLimit : PageWriteTools.DefaultLimit;

    [McpServerTool(Name = Tool, Title = "Create connection symbols and explicit wire paths", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates SPECIAL nodes, interruptions, wires, cables and CDP/CDPNG on new or existing Circuit connections. Verifies geometry, endpoints and properties. Undo: nodes only/interruptions/definition points 1; wires: 2 undo steps; cables: 6-8. Failure undoes the call. Omit dry_run to preview; false executes. Outside scratch needs approval. See usage. Example: {\"project\":\"Line1\",\"page\":\"=F+A1&EFS1/1\",\"items\":[{\"kind\":\"definition_point\",\"position\":[40,40],\"color\":\"RD\"}]}.")]
    public static async Task<CallToolResult> CreateAsync(WritePipeline pipeline, IBridgeClient bridge,
        string project, string page,
        [Description("1-50 wire/corner/t_node/potential_point/interruption_point/cable_definition/definition_point; see usage")] JsonElement[] items,
        bool dry_run = WriteToolDefaults.Preview, bool snap_to_grid = true, [AllowedValues("concise", "detailed")] string response_format = "concise",
        [Description("Open this page before drawing; UI only, no data change.")] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null, PackLibrary? packs = null, CancellationToken cancellationToken = default)
    {
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, project, page, cancellationToken).ConfigureAwait(false);
        try
        {
            PluginResolution? resolution = null;
            string[] plugins = [];
            List<string> warnings = [];
            JsonElement[] effective = items;
            if (packs is not null)
            {
                string key = PlanSnapshotHashes.ProjectKey(Path.IsPathFullyQualified(project) ? project
                    : (await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet, new() { Project = project }, cancellationToken).ConfigureAwait(false)).Project.Path);
                resolution = await new PluginResolver(packs).ResolveAsync(key, cancellationToken, page: page).ConfigureAwait(false);
                warnings.AddRange(resolution.Warnings);
                (effective, plugins) = ConnectionPluginDefaults.Apply(items, resolution);
            }
            JsonObject args = JsonSerializer.SerializeToNode(new { project, page, items = effective, dry_run, snap_to_grid, response_format })!.AsObject();
            if (plugins.Length > 0)
            { args["plugin_sources"] = JsonSerializer.SerializeToNode(plugins); }
            var request = new WriteRequest<object, object>(Tool, ToolTier.Write, OperationNames.ConnectionCreatePlan, OperationNames.ConnectionCreateExecute,
                args, plan => new { request = args, fingerprint = plan.Fingerprint }, dry_run, Limit(items), "Creating connections")
            {
                AuditArguments = new { requested = items, effective = args, plugins },
                BeforeExecute = packs is not null && resolution is not null ? PluginConsumerDefaults.BeforeExecute(packs, resolution, plugins, warnings) : null,
            };
            WriteOutcome<JsonObject> outcome = await pipeline.RunAsync<object, object, JsonObject>(request, progress, cancellationToken).ConfigureAwait(false);
            JsonObject result = outcome.Data ?? JsonNode.Parse(outcome.Plan.Details!["preview"])!.AsObject();
            result["execution"] = outcome.Execution;
            result["dryRun"] = dry_run;
            result["recovered"] = outcome.Recovered;
            if (dry_run)
            { result["hint"] = WriteToolDefaults.ExecutionHint; }
            if (plugins.Length > 0 || warnings.Count > 0)
            {
                JsonObject metadata = result["metadata"] as JsonObject ?? new JsonObject();
                if (plugins.Length > 0)
                { metadata["plugins"] = JsonSerializer.SerializeToNode(plugins); }
                if (warnings.Count > 0)
                { metadata["warnings"] = JsonSerializer.SerializeToNode(warnings); }
                result["metadata"] = metadata;
            }
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
            foreach (string field in new[] { "interruptionScope", "coordinates", "symbols", "graphicalAutoconnect", "logicalAfterGenerate", "actualEndpoints", "actualGraphicalAutoconnect", "interruptionPoints", "cableDefinitions", "coreImpact", "actualCores" })
            {
                if (ResultShaper.EstimateTokens(result.ToJsonString()) > budget)
                { result.Remove(field); result["truncated"] = true; }
            }
            if (result["definitionPoints"] is JsonArray definitionPoints && ResultShaper.EstimateTokens(result.ToJsonString()) > budget)
            {
                result["definitionPointCount"] = definitionPoints.Count;
                while (definitionPoints.Count > 1 && ResultShaper.EstimateTokens(result.ToJsonString()) > budget)
                { definitionPoints.RemoveAt(definitionPoints.Count - 1); result["truncated"] = true; }
            }
            return display.Apply(ToolResults.Success(dry_run ? "Dry run: connection and core impact predicted. Nothing was changed." : $"Connections created; {result["undoSteps"]} undo steps (Ctrl+Z).", result));
        }
        catch (BridgeCallException ex) { return display.Apply(ToolResults.Error(ex.Error)); }
    }
}

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
internal sealed class PlacementCreateTools
{
    public const string Tool = "eplan_placement_create";

    [McpServerTool(Name = Tool, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<CallToolResult> CreateAsync(IBridgeClient bridge, WritePipeline pipeline, RequestContext<CallToolRequestParams> context,
        [Description(PageDisplay.Description)] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        JsonElement input = JsonSerializer.SerializeToElement((context.Params?.Arguments ?? new Dictionary<string, JsonElement>()).Where(p => p.Key != "show_page").ToDictionary());
        List<string> problems = PlacementCreatePlanning.Validate(input);
        if (problems.Count > 0)
        { BridgeError error = ToolResults.InvalidArgumentError(problems); error.Execution = ExecutionState.NotStarted; return ToolResults.Error(error); }
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, input, cancellationToken).ConfigureAwait(false);
        if (input.TryGetProperty("dry_run", out JsonElement dry) && dry.GetBoolean())
        { return display.Apply(await PlacementCreatePlanning.PreviewAsync(bridge, input, cancellationToken: cancellationToken).ConfigureAwait(false)); }
        try
        {
            OpenProject project = (await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet,
                new() { Project = input.GetProperty("project").GetString() }, cancellationToken).ConfigureAwait(false)).Project;
            var pageArgs = new PageGetArgs { Project = project.Path, Page = input.GetProperty("page").GetString()! };
            PageGetData page = await bridge.InvokeAsync<PageGetArgs, PageGetData>(OperationNames.PageGet, pageArgs, cancellationToken).ConfigureAwait(false);
            if (page.Page.Type != "Circuit")
            { throw new BridgeCallException(ToolResults.InvalidArgumentError(["/page requires a Circuit page."])); }
            pageArgs.Page = page.Page.Name;
            PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, pageArgs, cancellationToken).ConfigureAwait(false);
            var args = new PlacementTextCreateArgs { Project = project.Path, Page = pageArgs.Page, Items = [], Fingerprint = geometry.Fingerprint() };
            bool snap = !input.TryGetProperty("snap_to_grid", out JsonElement s) || s.GetBoolean();
            var rows = new JsonArray();
            foreach (JsonElement item in input.GetProperty("items").EnumerateArray())
            {
                JsonNode actual = JsonNode.Parse(item.GetRawText())!;
                string kind = item.GetProperty("kind").GetString()!, field = kind == "text" ? "position" : kind == "line" ? "points" : "corners";
                double[][] points = kind == "text" ? [item.GetProperty(field).EnumerateArray().Select(n => n.GetDouble()).ToArray()]
                    : item.GetProperty(field).EnumerateArray().Select(p => p.EnumerateArray().Select(n => n.GetDouble()).ToArray()).ToArray();
                if (!double.IsFinite(geometry.GridSize) || geometry.GridSize <= 0)
                { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.CapabilityMissing, Message = "A reliable page grid is required." }); }
                foreach (double[] point in points)
                { for (int i = 0; i < 2; i++) { point[i] = snap ? Math.Round(point[i] / geometry.GridSize, MidpointRounding.AwayFromZero) * geometry.GridSize : point[i]; } }
                if (points.SelectMany(p => p).Any(n => !double.IsFinite(n) || n is < 0 or > 10000))
                { throw new BridgeCallException(ToolResults.InvalidArgumentError(["/items: snapped coordinates must be finite and in 0..10000 mm."])); }
                if (kind == "rectangle")
                { points = [[points.Min(p => p[0]), points.Min(p => p[1])], [points.Max(p => p[0]), points.Max(p => p[1])]]; }
                actual[field] = kind == "text" ? JsonSerializer.SerializeToNode(points[0]) : JsonSerializer.SerializeToNode(points);
                args.Items.Add(new() { Kind = kind, Position = points[0], EndPosition = kind == "text" ? [] : points[1], HeightMm = kind == "text" ? item.GetProperty("height_mm").GetDouble() : 0, Contents = kind == "text" ? item.GetProperty("contents").Deserialize<Dictionary<string, string>>()! : [] });
                rows.Add(new JsonObject { ["requested"] = JsonNode.Parse(item.GetRawText()), ["actual"] = actual, ["bounds"] = null, ["snapped"] = !JsonElement.DeepEquals(item, JsonSerializer.SerializeToElement(actual)) });
            }
            var plan = new WritePlan { Project = project.Name, ProjectPath = project.Path, Fingerprint = args.Fingerprint, Changes = rows.Select(row => new PlannedChange { Action = "create", Kind = "placement", Name = args.Page, NewValue = row!["actual"]!.ToJsonString() }).ToList() };
            var result = new JsonObject { ["project"] = project.Name, ["page"] = args.Page, ["execution"] = ExecutionState.NotStarted, ["dryRun"] = false, ["gridSize"] = geometry.GridSize, ["boundarySource"] = "unverified", ["items"] = rows, ["changes"] = JsonSerializer.SerializeToNode(plan.Changes.Select(c => c.NewValue)), ["connectionChanges"] = new JsonArray() };
            int budget = input.TryGetProperty("response_format", out JsonElement f) && f.GetString() == "detailed" ? 20000 : 5000;
            if (!ResultShaper.Fits(result, budget - 1024))
            { throw new BridgeCallException(ToolResults.InvalidArgumentError(["/items: reduce items or use detailed; the complete impact list exceeds the response budget."])); }
            var request = new WriteRequest<PlacementTextCreateArgs, PlacementTextCreateArgs>(Tool, ToolTier.Write, OperationNames.PlacementTextCreateExecute,
                OperationNames.PlacementTextCreateExecute, args, _ => args, false, TimeSpan.FromSeconds(60), "Creating text and graphics")
            { PreparedPlan = plan };
            WriteOutcome<PlacementTextCreateData> outcome = await pipeline.RunAsync<PlacementTextCreateArgs, PlacementTextCreateArgs, PlacementTextCreateData>(request, progress, cancellationToken).ConfigureAwait(false);
            result["execution"] = outcome.Execution;
            result["warning"] = ResultShaper.CleanText(outcome.Data?.Warning);
            result["recovered"] = outcome.Recovered;
            return display.Apply(ToolResults.Success("Created text/graphics; one Ctrl+Z undoes the batch.", ContextBuilder.Clean(result)));
        }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return display.Apply(ToolResults.Error(ex.Error)); }
    }
}

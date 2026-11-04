using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PlacementCreateTools
{
    public const string Tool = "eplan_placement_create";
    internal const string ShowPageDescription = "Open page (UI).";
    internal static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(180);

    [McpServerTool(Name = Tool, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    public static async Task<CallToolResult> CreateAsync(IBridgeClient bridge, WritePipeline pipeline, ImportFiles files, RequestContext<CallToolRequestParams> context,
        [Description(ShowPageDescription)] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        JsonElement input = JsonSerializer.SerializeToElement((context.Params?.Arguments ?? new Dictionary<string, JsonElement>()).Where(p => p.Key != "show_page").ToDictionary());
        List<string> problems = PlacementCreatePlanning.Validate(input);
        if (problems.Count > 0)
        { BridgeError error = ToolResults.InvalidArgumentError(problems); error.Execution = ExecutionState.NotStarted; return ToolResults.Error(error); }
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, input, cancellationToken).ConfigureAwait(false);
        if (WriteToolDefaults.IsPreview(input))
        { return display.Apply(await PlacementCreatePlanning.PreviewAsync(bridge, input, cancellationToken: cancellationToken, files: files).ConfigureAwait(false)); }
        try
        {
            OpenProject project = (await bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet,
                new() { Project = input.GetProperty("project").GetString() }, cancellationToken).ConfigureAwait(false)).Project;
            var pageArgs = new PageGetArgs { Project = project.Path, Page = input.GetProperty("page").GetString()! };
            PageGetData page = await bridge.InvokeAsync<PageGetArgs, PageGetData>(OperationNames.PageGet, pageArgs, cancellationToken).ConfigureAwait(false);
            if (!PlacementCreatePlanning.AllowedPageTypes.Contains(page.Page.Type, StringComparer.Ordinal))
            { throw new BridgeCallException(ToolResults.InvalidArgumentError([PlacementCreatePlanning.AllowedPageMessage])); }
            pageArgs.Page = page.Page.Name;
            PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, pageArgs, cancellationToken).ConfigureAwait(false);
            var args = new PlacementTextCreateArgs { Project = project.Path, Page = pageArgs.Page, Items = [], Fingerprint = geometry.Fingerprint(), LayoutGuard = input.TryGetProperty("layout_guard", out JsonElement guard) ? guard.GetString() : null };
            bool snap = !input.TryGetProperty("snap_to_grid", out JsonElement s) || s.GetBoolean();
            var rows = new JsonArray();
            foreach (JsonElement item in input.GetProperty("items").EnumerateArray())
            {
                JsonNode actual = PlacementCreatePlanning.Snap(item, geometry.GridSize, snap);
                args.Items.Add(await PlacementCreatePlanning.RequestAsync(JsonSerializer.SerializeToElement(actual), files, cancellationToken).ConfigureAwait(false));
                rows.Add(new JsonObject { ["requested"] = JsonNode.Parse(item.GetRawText()), ["actual"] = actual, ["bounds"] = null, ["snapped"] = !JsonElement.DeepEquals(item, JsonSerializer.SerializeToElement(actual)) });
            }
            PlacementTextCreateData checkedLayout = await bridge.InvokeAsync<PlacementTextCreateArgs, PlacementTextCreateData>(OperationNames.PlacementTextCreatePlan, args, CallLimit, null, cancellationToken).ConfigureAwait(false);
            var plan = new WritePlan { Project = project.Name, ProjectPath = project.Path, Fingerprint = args.Fingerprint, Details = new() { ["layoutWarnings"] = checkedLayout.Warning ?? "" }, Changes = rows.Select(row => new PlannedChange { Action = "create", Kind = "placement", Name = args.Page, NewValue = row!["actual"]!.ToJsonString() }).ToList() };
            var result = new JsonObject { ["project"] = project.Name, ["page"] = args.Page, ["execution"] = ExecutionState.NotStarted, ["dryRun"] = false, ["gridSize"] = geometry.GridSize, ["boundarySource"] = "unverified", ["items"] = rows, ["changes"] = JsonSerializer.SerializeToNode(plan.Changes.Select(c => c.NewValue)), ["connectionChanges"] = new JsonArray() };
            int budget = input.TryGetProperty("response_format", out JsonElement f) && f.GetString() == "detailed" ? 20000 : 5000;
            if (!ResultShaper.Fits(result, budget - 1024))
            { throw new BridgeCallException(ToolResults.InvalidArgumentError(["/items: reduce items or use detailed; the complete impact list exceeds the response budget."])); }
            var request = new WriteRequest<PlacementTextCreateArgs, PlacementTextCreateArgs>(Tool, ToolTier.Write, OperationNames.PlacementTextCreateExecute,
                OperationNames.PlacementTextCreateExecute, args, _ => args, false, CallLimit, "Creating text and graphics")
            { PreparedPlan = plan };
            WriteOutcome<PlacementTextCreateData> outcome = await pipeline.RunAsync<PlacementTextCreateArgs, PlacementTextCreateArgs, PlacementTextCreateData>(request, progress, cancellationToken).ConfigureAwait(false);
            result["execution"] = outcome.Execution;
            result["warning"] = ResultShaper.CleanText(outcome.Data?.Warning);
            result["recovered"] = outcome.Recovered;
            return display.Apply(ToolResults.Success("Created text/graphics; one Ctrl+Z undoes the batch.", ContextBuilder.Clean(result)));
        }
        catch (ImportFileException ex) { return display.Apply(ToolResults.Error(ex.Error)); }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return display.Apply(ToolResults.Error(ex.Error)); }
    }
}

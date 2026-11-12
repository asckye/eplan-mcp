using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Model views of the 3D mounting layout on layout pages (docs/tools/eplan_modelview_create.md), through <see cref="WritePipeline"/>.</summary>
[McpServerToolType]
internal sealed class ModelViewTools
{
    public const string CreateTool = "eplan_modelview_create";

    [McpServerTool(
        Name = CreateTool,
        Title = "Insert a model view of the 3D layout on a page",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ModelViewCreateResult))]
    [Description(
        "Inserts a 2D layout view of a space's panel, door or enclosure on a ModelView page. Approval outside scratch.")]
    public static async Task<CallToolResult> ModelViewCreateAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("Installation space, e.g. UC1")] string space,
        [Description("ModelView page full name")] string page,
        [Description("x, y, width, height")] double[] frame,
        [Description("panel (default), door, cabinet")] string? view = null,
        [Description("front (default)")] string? viewpoint = null,
        [Description("hidden_lines (default), wireframe, shaded")] string? style = null,
        [Description("auto (default), fit")] string? scale = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { violations.Add(ToolArguments.ProjectRequired); }
        if (string.IsNullOrWhiteSpace(space))
        { violations.Add("'space' is required: an installation space designation such as UC1."); }
        if (string.IsNullOrWhiteSpace(page))
        { violations.Add("'page' is required: the full name of a ModelView page from eplan_page_list."); }
        if (frame is not { Length: 4 })
        { violations.Add("'frame' must be [x, y, width, height]."); }
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }

        var args = new ModelViewCreateArgs
        {
            Project = project,
            Space = space,
            Page = page,
            View = ToolArguments.Optional(view),
            Viewpoint = ToolArguments.Optional(viewpoint),
            Style = ToolArguments.Optional(style),
            Scale = ToolArguments.Optional(scale),
            X = frame![0],
            Y = frame[1],
            Width = frame[2],
            Height = frame[3],
        };
        var request = new WriteRequest<ModelViewCreateArgs, ModelViewCreateArgs>(
            CreateTool,
            ToolTier.Write,
            OperationNames.ModelViewCreatePlan,
            OperationNames.ModelViewCreateExecute,
            args,
            plan => new ModelViewCreateArgs
            {
                Project = args.Project,
                Space = args.Space,
                Page = args.Page,
                View = args.View,
                Viewpoint = args.Viewpoint,
                Style = args.Style,
                Scale = args.Scale,
                X = args.X,
                Y = args.Y,
                Width = args.Width,
                Height = args.Height,
                Fingerprint = plan.Fingerprint,
            },
            dry_run,
            TimeSpan.FromSeconds(120),
            "Inserting the model view");
        WriteOutcome<ModelViewCreateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ModelViewCreateArgs, ModelViewCreateArgs, ModelViewCreateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        ModelViewCreateData? data = outcome.Data;
        var hints = new List<string>();
        if (dry_run)
        { hints.Add(WriteToolDefaults.ExecutionHint); }
        if (outcome.Recovered)
        { hints.Add("The response was lost, but EPLAN reported the change; open the page to check the view."); }
        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        { hints.Add(warning); }
        var result = new ModelViewCreateResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Page = ResultShaper.CleanText(data?.Page) ?? string.Empty,
            Name = ResultShaper.CleanText(data?.Name) ?? string.Empty,
            ObjectId = ResultShaper.CleanText(data?.ObjectId) ?? string.Empty,
            Added = data?.Added ?? 0,
            Undo = outcome.Execution == ExecutionState.Committed ? "AI: " + CreateTool : null,
            Recovered = outcome.Recovered,
            Hint = hints.Count == 0 ? null : string.Join(" ", hints),
        };
        string summary = outcome.Execution == ExecutionState.Committed
            ? $"Model view {result.Name} inserted on {result.Page}."
            : "Preview: " + ToolResults.Count(outcome.Plan.Changes.Count, "change") + ".";
        return ToolResults.Success(summary, result);
    }
}

using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Writes of the 3D mounting layout (docs/design/2026-11-12-m4b-layout3d.md), through <see cref="WritePipeline"/> like every write.</summary>
[McpServerToolType]
internal sealed class LayoutWriteTools
{
    public const string EnclosureTool = "eplan_enclosure_place";

    /// <summary>The time limit of the execution: one enclosure macro, about a second in the A136 spike.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    [McpServerTool(
        Name = EnclosureTool,
        Title = "Place an enclosure in the 3D mounting layout",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LayoutEnclosurePlaceResult))]
    [Description(
        "Places an enclosure by part in an installation space of the 3D layout, creating the space when needed; returns " +
        "its mounting panel and door sizes for placing rails, ducts and devices. Approval outside scratch; one undo step. " +
        "Read first: eplan_layout_get.")]
    public static async Task<CallToolResult> LayoutEnclosurePlaceAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("Installation space designation, e.g. UC1")] string space,
        [Description("Enclosure part number, e.g. RIT.1055500")] string part,
        [Description("Part variant; default 1")] string? variant = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { violations.Add(ToolArguments.ProjectRequired); }
        if (string.IsNullOrWhiteSpace(space))
        { violations.Add("'space' is required: an installation space designation such as UC1."); }
        if (string.IsNullOrWhiteSpace(part))
        { violations.Add("'part' is required: the enclosure's part number, e.g. RIT.1055500, from eplan_part_search."); }
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }

        var args = new LayoutEnclosurePlaceArgs { Project = project, Space = space, Part = part, Variant = ToolArguments.Optional(variant) };
        var request = new WriteRequest<LayoutEnclosurePlaceArgs, LayoutEnclosurePlaceArgs>(
            EnclosureTool,
            ToolTier.Write,
            OperationNames.LayoutEnclosurePlacePlan,
            OperationNames.LayoutEnclosurePlaceExecute,
            args,
            plan => new LayoutEnclosurePlaceArgs { Project = args.Project, Space = args.Space, Part = args.Part, Variant = args.Variant, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit,
            "Placing the enclosure");
        WriteOutcome<LayoutEnclosurePlaceData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<LayoutEnclosurePlaceArgs, LayoutEnclosurePlaceArgs, LayoutEnclosurePlaceData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        LayoutEnclosurePlaceData? data = outcome.Data;
        var hints = new List<string>();
        if (dry_run)
        { hints.Add(WriteToolDefaults.ExecutionHint); }
        if (outcome.Recovered)
        { hints.Add("The response was lost, but EPLAN reported the change; call eplan_layout_get to read the enclosure."); }
        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        { hints.Add(warning); }
        var result = new LayoutEnclosurePlaceResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Space = ResultShaper.CleanText(data?.Space),
            SpaceCreated = data?.SpaceCreated ?? false,
            Enclosure = data is null ? null : LayoutTools.Enclosure(data.Enclosure),
            Undo = outcome.Execution == ExecutionState.Committed ? TransactionUndo : null,
            Recovered = outcome.Recovered,
            Hint = hints.Count == 0 ? null : string.Join(" ", hints),
        };
        string summary = outcome.Execution == ExecutionState.Committed
            ? $"Enclosure placed in installation space {result.Space}."
            : "Preview: " + ToolResults.Count(outcome.Plan.Changes.Count, "change") + ".";
        return ToolResults.Success(summary, result);
    }

    private const string TransactionUndo = "AI: " + EnclosureTool;
}

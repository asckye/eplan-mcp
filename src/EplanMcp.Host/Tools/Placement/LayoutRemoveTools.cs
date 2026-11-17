using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Removes rails, ducts and device placements from the 3D layout (docs/tools/eplan_layout_remove.md).</summary>
[McpServerToolType]
internal sealed class LayoutRemoveTools
{
    public const string Tool = "eplan_layout_remove";

    [McpServerTool(
        Name = Tool,
        Title = "Remove rails, ducts and devices from the 3D layout",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LayoutRemoveResult))]
    [Description(
        "Removes rails, ducts and device placements (by object ID from eplan_layout_get) from an installation space; the " +
        "schematic devices stay and can be placed again. Approval outside scratch; one undo step.")]
    public static async Task<CallToolResult> LayoutRemoveAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("Installation space, e.g. UC1")] string space,
        [Description("1-200 object IDs; devices on a listed rail must be listed too")] long[] ids,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        { violations.Add(ToolArguments.ProjectRequired); }
        if (string.IsNullOrWhiteSpace(space))
        { violations.Add("'space' is required: an installation space designation such as UC1."); }
        if (ids is not { Length: > 0 })
        { violations.Add("'ids' is required: object IDs from eplan_layout_get."); }
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }

        var args = new LayoutRemoveArgs { Project = project, Space = space, Ids = [.. ids] };
        var request = new WriteRequest<LayoutRemoveArgs, LayoutRemoveArgs>(
            Tool,
            ToolTier.Write,
            OperationNames.LayoutRemovePlan,
            OperationNames.LayoutRemoveExecute,
            args,
            plan => new LayoutRemoveArgs { Project = args.Project, Space = args.Space, Ids = args.Ids, Fingerprint = plan.Fingerprint },
            dry_run,
            TimeSpan.FromSeconds(30 + ids.Length),
            "Removing layout objects");
        WriteOutcome<LayoutRemoveData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<LayoutRemoveArgs, LayoutRemoveArgs, LayoutRemoveData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        LayoutRemoveData? data = outcome.Data;
        var hints = new List<string>();
        if (dry_run)
        { hints.Add(WriteToolDefaults.ExecutionHint); }
        if (outcome.Recovered)
        { hints.Add("The response was lost, but EPLAN reported the change; call eplan_layout_get to check."); }
        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        { hints.Add(warning); }
        var result = new LayoutRemoveResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Removed = data is null ? [] : [.. data.Removed.Select(r => new LayoutRemoveResult.Entry
            {
                Id = r.Id,
                Kind = r.Kind,
                Tag = ResultShaper.CleanText(r.Tag),
                Parts = ResultShaper.CleanTexts(r.Parts),
            })],
            Undo = outcome.Execution == ExecutionState.Committed ? "AI: " + Tool : null,
            Recovered = outcome.Recovered,
            Hint = hints.Count == 0 ? null : string.Join(" ", hints),
        };
        string summary = outcome.Execution == ExecutionState.Committed
            ? $"Removed {ToolResults.Count(result.Removed.Count, "object")}."
            : "Preview: " + ToolResults.Count(outcome.Plan.Changes.Count, "object") + " to remove.";
        return ToolResults.Success(summary, result);
    }
}

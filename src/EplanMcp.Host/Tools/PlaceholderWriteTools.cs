using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Applies a placeholder's value set (docs/tools/eplan_placeholder_apply.md) through <see cref="WritePipeline"/>: plan,
/// policy, approval, audit, one execution that is never sent twice. The Bridge chooses the placeholder and reads it back.
/// </summary>
[McpServerToolType]
internal sealed class PlaceholderWriteTools
{
    public const string ApplyTool = "eplan_placeholder_apply";

    [McpServerTool(
        Name = ApplyTool,
        Title = "Apply a placeholder's value set in an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PlaceholderApplyResult))]
    [Description(
        "Applies one value set to a placeholder on a page, so EPLAN writes its values into the objects the placeholder " +
        "references; one Ctrl+Z in EPLAN undoes it. Find value sets with eplan_placeholder_list. Outside scratch it needs " +
        "approval. Example: {\"project\": \"Line1\", \"page\": \"=TM1+A1&EFS1/1\", \"value_set\": \"mA\"}.")]
    public static async Task<CallToolResult> PlaceholderApplyAsync(
        WritePipeline pipeline,
        [Description("From eplan_project_list")] string project,
        [Description("Full page name")] string page,
        [Description("From valueSets, case-sensitive")] string value_set,
        [Description("Its name; \"\" for none; default: any")] string? placeholder = null,
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The page, the placeholder and the value set are checked by the Bridge, all at once (placeholder.apply.plan).
        if (string.IsNullOrWhiteSpace(project))
        {
            return ToolResults.InvalidArguments(["'project' is required for writes: a name or path from eplan_project_list."]);
        }

        var args = new PlaceholderApplyArgs { Project = project, Page = page ?? string.Empty, Placeholder = placeholder, ValueSet = value_set ?? string.Empty };
        var request = new WriteRequest<PlaceholderApplyArgs, PlaceholderApplyArgs>(
            ApplyTool,
            ToolTier.Write,
            OperationNames.PlaceholderApplyPlan,
            OperationNames.PlaceholderApplyExecute,
            args,
            plan => new PlaceholderApplyArgs { Project = args.Project, Page = args.Page, Placeholder = args.Placeholder, ValueSet = args.ValueSet, Fingerprint = plan.Fingerprint },
            dry_run,
            PageWriteTools.DefaultLimit,
            "Applying the value set");
        WriteOutcome<PlaceholderApplyData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PlaceholderApplyArgs, PlaceholderApplyArgs, PlaceholderApplyData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        PlaceholderApplyData? data = outcome.Data;
        var result = new PlaceholderApplyResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Placeholder = new PlaceholderApplyResult.PlaceholderRef
            {
                Name = ResultShaper.CleanText(data?.Placeholder ?? details.GetValueOrDefault("placeholder")) ?? string.Empty,
                Page = ResultShaper.CleanText(data?.Page ?? details.GetValueOrDefault("page")) ?? string.Empty,
            },
            ValueSet = ResultShaper.CleanText(data?.ValueSet ?? args.ValueSet),
            Values = data is null ? PlannedValues(details) : [.. data.Values.Select(v => PlaceholderTools.Value(v, withNames: true))],
            CurrentValuesBefore = data?.CurrentValuesBefore.Select(v => PlaceholderTools.Value(v, withNames: false)).ToList(),
            CurrentValuesAfter = data?.CurrentValuesAfter.Select(v => PlaceholderTools.Value(v, withNames: false)).ToList(),
            CurrentValuesChanged = data?.CurrentValuesChanged,
            Undo = committed ? "AI: " + ApplyTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the value set as applied; check the placeholder with eplan_placeholder_list."
                : ResultShaper.CleanText(data?.Warning)
                    ?? (data is { CurrentValuesChanged: false } ? "The current values did not change: the value set may have been applied already. Check the referenced objects in EPLAN if it matters." : null),
        };
        string summary = !committed ? "Dry run: would apply the value set to the placeholder. Nothing was changed."
            : data is null ? "Applied the value set; Ctrl+Z in EPLAN undoes this step."
            : $"Applied the value set to the placeholder; its current values {(data.CurrentValuesChanged ? "changed" : "did not change")}. Ctrl+Z in EPLAN undoes this step.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The plan's <c>values</c> details, one <c>variable TAB text</c> per line (placeholder.apply.plan).</summary>
    private static List<PlaceholderListResult.ValueEntry> PlannedValues(Dictionary<string, string> details) =>
    [
        .. Lines(details, "values").Select(line => line.Split('\t', 2)).Select(fields => new PlaceholderListResult.ValueEntry
        {
            Variable = ResultShaper.CleanText(fields[0]),
            Text = fields.Length > 1 && fields[1].Length > 0 ? ResultShaper.CleanText(fields[1]) : null,
        }),
    ];

    private static string[] Lines(Dictionary<string, string> details, string key) =>
        details.GetValueOrDefault(key) is { Length: > 0 } text ? text.Split('\n') : [];
}

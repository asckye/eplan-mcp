using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Updating reports (docs/tools/eplan_report_update.md), through <see cref="WritePipeline"/> like every write: report
/// pages are made from the project's data and can be made again, and a SafetyPoint takes an update back (M4 design Q3).
/// </summary>
[McpServerToolType]
internal sealed class ReportWriteTools
{
    public const string UpdateTool = "eplan_report_update";

    /// <summary>The time limit of the execution (M4 design 4.1); S11 updated the 321-page sample project's reports in under 10 s.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = UpdateTool,
        Title = "Update the reports of an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ReportUpdateResult))]
    [Description(
        "Updates the reports of an EPLAN project, all of them or given report pages, after devices, connections or pages " +
        "changed; lists the report pages added and removed. One Ctrl+Z in EPLAN undoes it. Takes seconds; EPLAN does not " +
        "respond meanwhile. Outside scratch it needs the engineer's approval. Example: {\"project\": \"Line1\"}.")]
    public static async Task<CallToolResult> ReportUpdateAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("1-50 report page names; omit for all")] string[]? pages = null,
        [Description("Only count the report pages now")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        // The page names, and that they are report pages, are checked by the Bridge, all at once (report.update.plan).
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (pages is { Length: < 1 or > PageWriteTools.MaxPages })
        {
            violations.Add($"'pages' must hold 1 to {PageWriteTools.MaxPages} full page names; omit it for all reports.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new ReportUpdateArgs { Project = project, Pages = pages is null ? null : [.. pages.Select(p => p ?? string.Empty)] };
        var request = new WriteRequest<ReportUpdateArgs, ReportUpdateArgs>(
            UpdateTool,
            ToolTier.Write,
            OperationNames.ReportUpdatePlan,
            OperationNames.ReportUpdateExecute,
            args,
            plan => new ReportUpdateArgs { Project = args.Project, Pages = args.Pages, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit);
        WriteOutcome<ReportUpdateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ReportUpdateArgs, ReportUpdateArgs, ReportUpdateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return outcome.Data is { } data ? Updated(data, args) : Planned(outcome, args, dry_run);
    }

    /// <summary>A commit that answered in time: the report pages added and removed, at most 100 listed (tool spec 3).</summary>
    private static CallToolResult Updated(ReportUpdateData data, ReportUpdateArgs args)
    {
        int listed = data.Added.Count + data.Removed.Count;
        int changed = data.AddedCount + data.RemovedCount;
        var result = new ReportUpdateResult
        {
            Project = ResultShaper.CleanText(data.Project),
            Execution = ExecutionState.Committed,
            Scope = data.Scope,
            ReportPages = new ReportUpdateResult.ReportPagesEntry { Before = data.Before, After = data.After },
            Added = [.. data.Added.Select(name => ResultShaper.CleanText(name))],
            Removed = [.. data.Removed.Select(name => ResultShaper.CleanText(name))],
            Undo = "AI: " + UpdateTool,
            Truncated = listed < changed,
        };
        string?[] hints =
        [
            ResultShaper.CleanText(data.Warning),
            result.Truncated
                ? string.Create(CultureInfo.InvariantCulture, $"Listed {listed} of {changed} added and removed report pages, the removed ones first; eplan_page_list shows the project's pages.")
                : null,
        ];
        result.Hint = string.Join(" ", hints.OfType<string>()) is { Length: > 0 } text ? text : null;
        // As the spec words it: "1 report page added" after all reports, "0 added" after the given report pages.
        string added = args.Pages is null ? ToolResults.Count(data.AddedCount, "report page") : data.AddedCount.ToString(CultureInfo.InvariantCulture);
        string counts = $"{added} added, {data.RemovedCount.ToString(CultureInfo.InvariantCulture)} removed";
        return ToolResults.Success($"Updated {Scope(args)}: {counts}; Ctrl+Z in EPLAN undoes this step.", result);
    }

    /// <summary>A dry run, or a commit whose response was lost: the scope and the report pages before, from the plan.</summary>
    private static CallToolResult Planned(WriteOutcome<ReportUpdateData> outcome, ReportUpdateArgs args, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        int before = int.TryParse(outcome.Plan.Details?.GetValueOrDefault("reportPages"), NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 0;
        string scope = args.Pages is null ? ReportUpdateData.ProjectScope : ReportUpdateData.PagesScope;
        var result = new ReportUpdateResult
        {
            Project = ResultShaper.CleanText(outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Scope = scope,
            ReportPages = new ReportUpdateResult.ReportPagesEntry { Before = before },
            Undo = committed ? "AI: " + UpdateTool : null,
            Recovered = outcome.Recovered,
            Hint = committed
                ? "The response was lost, but EPLAN reported the reports as updated; eplan_page_list shows the project's pages."
                : "Which report pages EPLAN creates or removes is known only after running, so 'after', 'added' and 'removed' are null.",
        };
        string what = Scope(args);
        return ToolResults.Success(
            committed
                ? $"Updated {what}; Ctrl+Z in EPLAN undoes this step."
                : $"Dry run: would update {what} ({ToolResults.Count(before, "report page")} now); the pages added or removed are known only after running. Nothing was changed.",
            result);
    }

    /// <summary>"all reports", or "2 report pages" for given pages.</summary>
    private static string Scope(ReportUpdateArgs args) => args.Pages is { } pages ? ToolResults.Count(pages.Count, "report page") : "all reports";
}

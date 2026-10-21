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
/// Running EPLAN's check (docs/tools/eplan_check_run.md), through <see cref="WritePipeline"/> like every write: the check
/// replaces the project's check messages (M4 design Q3). Reading the messages without a check is eplan_check_list.
/// </summary>
[McpServerToolType]
internal sealed class CheckWriteTools
{
    public const string RunTool = "eplan_check_run";

    /// <summary>The time limit of the execution (M4 design 4.1); S11 checked the 321-page sample project in under 5 s.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    [McpServerTool(
        Name = RunTool,
        Title = "Run the check of an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        // Each run adds EPLAN's summary hints again (live check, api-notes A20).
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(CheckRunResult))]
    [Description(
        "Runs EPLAN's check on a project or some of its pages and returns the counts per severity and the first 20 " +
        "messages. EPLAN replaces earlier messages, adds its summary hints, and for pages drops messages of other objects; " +
        "one Ctrl+Z in EPLAN undoes it. Takes seconds; EPLAN does not " +
        "respond meanwhile. Outside scratch it needs the engineer's approval. Example: {\"project\": \"Line1\"}.")]
    public static async Task<CallToolResult> CheckRunAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("1-50 full page names; omit for all")] string[]? pages = null,
        [Description("Check scheme; omit for EPLAN's current")] string? scheme = null,
        [Description("Only count the messages now")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        // The page names are checked by the Bridge, all at once (check.run.plan).
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (pages is { Length: < 1 or > PageWriteTools.MaxPages })
        {
            violations.Add($"'pages' must hold 1 to {PageWriteTools.MaxPages} full page names; omit it for the whole project.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new CheckRunArgs { Project = project, Pages = pages is null ? null : [.. pages.Select(p => p ?? string.Empty)], Scheme = ToolArguments.Optional(scheme) };
        var request = new WriteRequest<CheckRunArgs, CheckRunArgs>(
            RunTool,
            ToolTier.Write,
            OperationNames.CheckRunPlan,
            OperationNames.CheckRunExecute,
            args,
            plan => new CheckRunArgs { Project = args.Project, Pages = args.Pages, Scheme = args.Scheme, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit,
            "Running the EPLAN check");
        WriteOutcome<CheckRunData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<CheckRunArgs, CheckRunArgs, CheckRunData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return outcome.Data is { } data ? Checked(data, pages is not null) : Planned(outcome, dry_run);
    }

    /// <summary>A commit that answered in time: the messages read back, the first 20 listed (tool spec 3).</summary>
    private static CallToolResult Checked(CheckRunData data, bool somePages)
    {
        var result = new CheckRunResult
        {
            Project = ResultShaper.CleanText(data.Project),
            Execution = ExecutionState.Committed,
            PageCount = data.PageCount,
            Scheme = ResultShaper.CleanText(data.Scheme),
            Before = data.Before,
            Counts = new CheckListResult.CountsEntry { Error = data.Errors, Warning = data.Warnings, Hint = data.Hints },
            Messages = [.. data.Messages.Select(CheckTools.Entry)],
            Total = data.Total,
            Undo = "AI: " + RunTool,
        };
        string? warning = Join(
            ResultShaper.CleanText(data.Warning),
            somePages
                ? "Checking pages also removed earlier messages of objects outside them, such as 3D connections; run eplan_check_run without 'pages' to check the whole project again."
                : null);
        Shape(result, warning);
        if (ResultShaper.FitItems(result, result.Messages) > 0)
        {
            Shape(result, warning);
        }

        string[] found =
        [
            .. new[] { (Count: data.Errors, Noun: "error"), (Count: data.Warnings, Noun: "warning"), (Count: data.Hints, Noun: "hint") }
                .Where(c => c.Count > 0)
                .Select(c => ToolResults.Count(c.Count, c.Noun)),
        ];
        string what = found.Length > 0 ? string.Join(", ", found) : data.Total > 0 ? ToolResults.Count(data.Total, "message") : "no messages";
        return ToolResults.Success($"Check found {what} ({ToolResults.Count(data.Before, "message")} before); Ctrl+Z in EPLAN undoes this step.", result);
    }

    private static string? Join(params string?[] parts) => string.Join(" ", parts.OfType<string>()) is { Length: > 0 } text ? text : null;

    /// <summary>The truncation flag and the hint for the messages listed now.</summary>
    private static void Shape(CheckRunResult result, string? warning)
    {
        int listed = result.Messages!.Count;
        result.Truncated = listed < result.Total;
        string?[] hints =
        [
            ResultShaper.CleanText(warning),
            result.Truncated
                ? string.Create(CultureInfo.InvariantCulture, $"Listed {listed} of {result.Total} messages; eplan_check_list lists them all, by 'severity' and with 'cursor'.")
                : null,
        ];
        result.Hint = string.Join(" ", hints.OfType<string>()) is { Length: > 0 } text ? text : null;
    }

    /// <summary>A dry run, or a commit whose response was lost: the scope and the count before, from the plan.</summary>
    private static CallToolResult Planned(WriteOutcome<CheckRunData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        PlannedChange? change = outcome.Plan.Changes.FirstOrDefault();
        var result = new CheckRunResult
        {
            Project = ResultShaper.CleanText(outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            PageCount = Number(details.GetValueOrDefault("pageCount")),
            Scheme = ResultShaper.CleanText(details.GetValueOrDefault("scheme")),
            Before = Number(change?.OldValue),
            Undo = committed ? "AI: " + RunTool : null,
            Recovered = outcome.Recovered,
            Hint = committed
                ? "The response was lost, but EPLAN reported the check as committed; read the messages with eplan_check_list."
                : "The new messages are known only after running; eplan_check_list lists the ones now.",
        };
        string pages = ToolResults.Count(result.PageCount, "page");
        return ToolResults.Success(
            committed
                ? $"Ran the check on {pages}; Ctrl+Z in EPLAN undoes this step."
                : $"Dry run: would check {pages} and replace the {ToolResults.Count(result.Before, "existing check message")}. Nothing was changed.",
            result);
    }

    private static int Number(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;
}

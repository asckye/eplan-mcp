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
/// The Destructive page tools (docs/tools/eplan_page_delete.md). Every call goes through <see cref="WritePipeline"/> at
/// <see cref="ToolTier.Destructive"/>: the engineer approves each one, in scratch too, and the project is backed up
/// before anything changes (ADR-0005, ADR-0013). The Bridge resolves the pages; the Host shapes the result.
/// </summary>
[McpServerToolType]
internal sealed class PageDestructiveTools
{
    public const string DeleteTool = "eplan_page_delete";

    private const string ReportHint = "Reports are not updated when pages are deleted; call eplan_report_update.";

    [McpServerTool(
        Name = DeleteTool,
        Title = "Delete pages from an EPLAN project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PageDeleteResult))]
    [Description(
        "Deletes pages by their exact full names. Always needs the engineer's approval, in scratch too; the project is " +
        "backed up first, and one Ctrl+Z in EPLAN undoes it. Lists devices left without their main function. Example: " +
        "{\"project\": \"Line1\", \"pages\": [\"=GAA+A1&EFS1/60\"]}.")]
    public static async Task<CallToolResult> PageDeleteAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")] string project,
        [Description("1-50 exact full names")] string[] pages,
        [Description("Only return the plan")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (pages is null or { Length: < 1 or > PageWriteTools.MaxPages })
        {
            violations.Add($"'pages' must hold 1 to {PageWriteTools.MaxPages} full page names.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new PageDeleteArgs { Project = project, Pages = [.. pages!.Select(p => p ?? string.Empty)] };
        var request = new WriteRequest<PageDeleteArgs, PageDeleteArgs>(
            DeleteTool,
            ToolTier.Destructive,
            OperationNames.PageDeletePlan,
            OperationNames.PageDeleteExecute,
            args,
            (plan, backup) => new PageDeleteArgs { Project = args.Project, Pages = args.Pages, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            PageWriteTools.Limit(args.Pages.Count),
            "Deleting pages");
        WriteOutcome<PageDeleteData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PageDeleteArgs, PageDeleteArgs, PageDeleteData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Deleted(outcome, dry_run);
    }

    /// <summary>The result: read back after a commit; from the plan's details for a dry run or a recovered commit.</summary>
    private static CallToolResult Deleted(WriteOutcome<PageDeleteData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        PageDeleteData? data = outcome.Data;
        List<PageDeleteResult.PageEntry> pages = data is null
            ? [.. Lines(details, "pages").Select(f => Page(f[0], f.Length > 1 ? f[1] : string.Empty, f.Length > 2 ? Number(f[2]) : 0))]
            : [.. data.Pages.Select(p => Page(p.Name, p.Type, p.Functions))];
        List<PageDeleteResult.OrphanEntry> orphaned = data is null
            ? [.. Lines(details, "orphaned").Select(f => Orphan(f[0], f.Length > 1 ? Number(f[1]) : 0, f.Skip(2)))]
            : [.. data.Orphaned.Select(o => Orphan(o.Tag, o.FunctionsLeft, o.Pages))];
        int total = data?.OrphanedCount ?? Number(details.GetValueOrDefault("orphanedCount") ?? "0");
        var hints = new List<string> { ReportHint };
        if (total > orphaned.Count)
        {
            hints.Add($"{ToolResults.Count(total, "device")} lose their main function; only the first {orphaned.Count.ToString(CultureInfo.InvariantCulture)} are listed.");
        }

        if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the pages as deleted; the lists are as planned, so check with eplan_page_list.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        var result = new PageDeleteResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Pages = pages,
            Orphaned = orphaned,
            Backup = committed ? outcome.Backup : null,
            Undo = committed ? "AI: " + DeleteTool : null,
            Recovered = outcome.Recovered,
            Truncated = total > orphaned.Count,
            Hint = string.Join(" ", hints),
        };
        string count = ToolResults.Count(pages.Count, "page");
        string devices = ToolResults.Count(total, "device");
        return ToolResults.Success(
            committed
                ? $"Deleted {count}{(total == 0 ? string.Empty : $"; {devices} {(total == 1 ? "keeps" : "keep")} functions on other pages without their main function")}. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step."
                : $"Dry run: would delete {count} ({ToolResults.Count(pages.Sum(p => p.Functions), "function")}){(total == 0 ? string.Empty : $"; {devices} would keep functions on other pages without their main function")}. Nothing was changed.",
            result);
    }

    /// <summary>The tab-separated lines of a plan's details entry (page.delete.plan).</summary>
    private static IEnumerable<string[]> Lines(Dictionary<string, string> details, string key) =>
        details.GetValueOrDefault(key) is { Length: > 0 } text ? text.Split('\n').Select(line => line.Split('\t')) : [];

    private static int Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;

    private static PageDeleteResult.PageEntry Page(string name, string type, int functions) => new()
    {
        Name = ResultShaper.CleanText(name),
        Type = ResultShaper.CleanText(type),
        Functions = functions,
    };

    private static PageDeleteResult.OrphanEntry Orphan(string tag, int left, IEnumerable<string> pages) => new()
    {
        Tag = ResultShaper.CleanText(tag),
        FunctionsLeft = left,
        Pages = [.. pages.Select(page => ResultShaper.CleanText(page))],
    };
}

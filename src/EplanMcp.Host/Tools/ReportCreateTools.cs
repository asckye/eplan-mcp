using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class ReportCreateTools
{
    public const string Tool = "eplan_report_create";

    [McpServerTool(Name = Tool, Title = "Create report pages from a report block", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ReportCreateResult))]
    [Description("Preview by default; Ctrl+Z undo. Approval outside scratch (APPROVAL_DENIED).")]
    public static async Task<CallToolResult> CreateAsync(WritePipeline pipeline, IProgress<ProgressNotificationValue> progress,
        string project,
        [AllowedValues("TitlePage", "TableOfContents", "PLCDiagram", "TerminalDiagram", "PartsSumList", "PartsList", "CableOverview", "CableConnectionDiagram", "TerminalStripOverview", "DeviceList", "ConnectionList", "StructIdentifierOverview")] string type,
        string form, string first_page,
        string? filter_scheme = null, string? sort_scheme = null,
        string? page_description = null,
        string? filter_scheme_data = null, string? sort_scheme_data = null,
        string? function_filter_scheme_data = null, string? function_sort_scheme_data = null,
        bool dry_run = true, CancellationToken cancellationToken = default)
    {
        var args = new ReportCreateArgs
        {
            Project = project,
            Type = type,
            Form = form,
            FirstPage = first_page,
            FilterScheme = filter_scheme,
            SortScheme = sort_scheme,
            PageDescription = page_description,
            FilterSchemeData = filter_scheme_data,
            SortSchemeData = sort_scheme_data,
            FunctionFilterSchemeData = function_filter_scheme_data,
            FunctionSortSchemeData = function_sort_scheme_data,
        };
        List<string> violations = args.Validate();
        if (violations.Count > 0)
        { return ToolResults.InvalidArguments(violations); }
        try
        {
            var request = new WriteRequest<ReportCreateArgs, ReportCreateArgs>(Tool, ToolTier.Write, OperationNames.ReportCreatePlan, OperationNames.ReportCreateExecute,
                args, plan => new ReportCreateArgs
                {
                    Project = project,
                    Type = type,
                    Form = form,
                    FirstPage = first_page,
                    FilterScheme = filter_scheme,
                    SortScheme = sort_scheme,
                    PageDescription = page_description,
                    FilterSchemeData = filter_scheme_data,
                    SortSchemeData = sort_scheme_data,
                    FunctionFilterSchemeData = function_filter_scheme_data,
                    FunctionSortSchemeData = function_sort_scheme_data,
                    Fingerprint = plan.Fingerprint,
                }, dry_run, ReportWriteTools.Limit, "Creating report pages");
            WriteOutcome<ReportCreateData> outcome = await pipeline.RunAsync<ReportCreateArgs, ReportCreateArgs, ReportCreateData>(request, progress, cancellationToken).ConfigureAwait(false);
            bool committed = outcome.Execution == ExecutionState.Committed;
            var result = new ReportCreateResult
            {
                Project = ResultShaper.CleanText(outcome.Plan.Project),
                Execution = outcome.Execution,
                DryRun = dry_run,
                Changes = [.. WritePipeline.ChangeLines(outcome.Plan).Select(p => ResultShaper.CleanText(p))],
                Fingerprint = outcome.Plan.Fingerprint,
                Type = ResultShaper.CleanText(type),
                Form = ResultShaper.CleanText(form),
                Pages = outcome.Data?.Pages.Select(p => ResultShaper.CleanText(p)).ToList(),
                Count = outcome.Data?.Pages.Count,
                NewPages = outcome.Data?.NewPages.Select(p => ResultShaper.CleanText(p)).ToList(),
                Undo = committed ? "AI: " + Tool : null,
                Recovered = outcome.Recovered,
                Hint = outcome.Data is { } data ? Hint(data) : "Page names and count are known only after generation; use eplan_page_list to inspect a recovered commit.",
            };
            return ToolResults.Success(committed ? (result.Count is { } count ? $"Report generation completed: {ToolResults.Count(count, "matching report page")}; Ctrl+Z in EPLAN undoes this step." : "Created report pages; Ctrl+Z in EPLAN undoes this step.") : "Dry run: would create report pages. Nothing was changed.", result);
        }
        catch (BridgeCallException ex) { return ToolResults.Error(ex.Error); }
    }

    private static string? Hint(ReportCreateData data)
    {
        string? warning = ResultShaper.CleanText(data.Warning);
        return data.NewPages.Count == 0 && data.Pages.Count > 0
            ? "Existing report pages were overwritten; no new page names were added." + (warning is null ? string.Empty : " " + warning)
            : warning;
    }
}

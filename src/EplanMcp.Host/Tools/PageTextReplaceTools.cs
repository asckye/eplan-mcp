using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PageTextReplaceTools
{
    public const string Tool = "eplan_page_text_replace";

    [McpServerTool(Name = Tool, Title = "Replace page free texts", ReadOnly = false, Destructive = false,
        Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PageTextReplaceResult))]
    [Description("Replaces literals in page/group free texts; excludes frames and other text types. One Ctrl+Z undoes it. " +
        "Outside scratch needs approval; denial: APPROVAL_DENIED. " +
        "Example: {\"project\":\"Line1\",\"pages\":[\"=F+A2&EFA1/2\"],\"find\":\"Motor\",\"replace\":\"Drive\",\"language\":\"en_US\"}.")]
    public static async Task<CallToolResult> ReplaceAsync(
        WritePipeline pipeline,
        [Description("Project name/path")] string project,
        [Description("Exact full names"), MinLength(1), MaxLength(50)] string[] pages,
        [Description("Literal, no regex"), MinLength(1), MaxLength(500)] string find,
        [Description("Empty deletes"), MaxLength(500)] string replace,
        [Description("Code, ??_?? or all")] string language,
        bool case_sensitive = true,
        [Range(1, 500)] int max_changes = 500,
        bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<string> violations = PageWriteTools.Violations(project, pages?.Length, null);
        if (find is not { Length: >= 1 and <= 500 } || replace is not { Length: <= 500 } || string.IsNullOrWhiteSpace(language) || max_changes is < 1 or > 500)
        {
            violations.Add("Provide a nonempty 'find' (max 500 characters), 'replace' (max 500, empty allowed), explicit 'language', and 'max_changes' between 1 and 500.");
        }

        if (violations.Count > 0)
        {
            BridgeError error = ToolResults.InvalidArgumentError(violations);
            error.Execution = ExecutionState.NotStarted;
            return ToolResults.Error(error);
        }

        var args = new PageTextReplaceArgs
        {
            Project = project,
            Pages = [.. pages!],
            Find = find,
            Replace = replace,
            Language = language,
            CaseSensitive = case_sensitive,
            MaxChanges = max_changes,
        };
        var request = new WriteRequest<PageTextReplaceArgs, PageTextReplaceArgs>(Tool, ToolTier.Write,
            OperationNames.PageTextReplacePlan, OperationNames.PageTextReplaceExecute, args,
            plan => new PageTextReplaceArgs
            {
                Project = args.Project,
                Pages = args.Pages,
                Find = args.Find,
                Replace = args.Replace,
                Language = args.Language,
                CaseSensitive = args.CaseSensitive,
                MaxChanges = args.MaxChanges,
                Fingerprint = plan.Fingerprint,
            }, dry_run, PageWriteTools.DefaultLimit, "Replacing page free texts");
        WriteOutcome<PageTextReplaceData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PageTextReplaceArgs, PageTextReplaceArgs, PageTextReplaceData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        List<PageTextChange> raw = outcome.Data?.Changes ?? outcome.Plan.PageTextChanges!;
        bool committed = outcome.Execution == ExecutionState.Committed;
        string? warning = outcome.Recovered ? "EPLAN confirmed the commit after its response was lost; changes show the original plan." : ResultShaper.CleanText(outcome.Data?.Warning);
        var result = new PageTextReplaceResult
        {
            Project = ResultShaper.CleanText(outcome.Data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            TotalChanges = raw.Count,
            TotalOccurrences = raw.Sum(c => (long)c.Occurrences),
            Undo = committed ? "AI: " + Tool : null,
            Recovered = outcome.Recovered,
            Changes = [.. raw.Select(c => new PageTextChange
            {
                Page = ResultShaper.CleanText(c.Page),
                Oid = c.Oid,
                Language = ResultShaper.CleanText(c.Language),
                Before = ResultShaper.CleanText(c.Before),
                After = ResultShaper.CleanText(c.After),
                Occurrences = c.Occurrences,
            })],
            Truncated = ResultShaper.CleanFullText(outcome.Data?.Project ?? outcome.Plan.Project).Length > ResultShaper.MaxTextLength
                || raw.Any(c => new[] { c.Page, c.Language, c.Before, c.After }.Any(v => ResultShaper.CleanFullText(v).Length > ResultShaper.MaxTextLength)),
            OmittedChanges = raw.Count,
            Hint = warning + " Some text or rows were shortened; narrow pages or language in a new dry run. Do not repeat a committed replacement.",
        };
        result.OmittedChanges = ResultShaper.FitItems(result, result.Changes, ResultShaper.DefaultTokenBudget - 128);
        result.Truncated |= result.OmittedChanges > 0;
        // MCP transports may escape Unicode in structuredContent; budget that representation too.
        while (result.Changes.Count > 0 && ResultShaper.ResultTokens(JsonSerializer.Serialize(result)) > ResultShaper.DefaultTokenBudget - 128)
        {
            result.Changes.RemoveAt(result.Changes.Count - 1);
            result.OmittedChanges++;
            result.Truncated = true;
        }

        if (!result.Truncated)
        {
            result.Hint = warning;
        }

        string count = string.Create(CultureInfo.InvariantCulture, $"{result.TotalChanges} text-language {(result.TotalChanges == 1 ? "entry" : "entries")} ({result.TotalOccurrences} {(result.TotalOccurrences == 1 ? "occurrence" : "occurrences")})");
        return ToolResults.Success(committed ? $"Changed {count}; Ctrl+Z in EPLAN undoes this step." : $"Dry run: would change {count}. Nothing was changed.", result);
    }
}

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
/// The connection write tools (docs/tools/eplan_connection_generate.md), through <see cref="WritePipeline"/> like every
/// write. Connections are generated on given pages only and updated, never rebuilt (M3b design 4.3, Q4).
/// </summary>
[McpServerToolType]
internal sealed class ConnectionWriteTools
{
    public const string GenerateTool = "eplan_connection_generate";

    [McpServerTool(
        Name = GenerateTool,
        Title = "Generate connections on pages of an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ConnectionGenerateResult))]
    [Description(
        "Generates connections on pages of an EPLAN project: lines drawn between connection points, for example after a " +
        "macro was placed, become connections that wire lists and reports show. Updates only; one Ctrl+Z in EPLAN undoes " +
        "it. Outside scratch it needs the engineer's approval. Example: {\"project\": \"Line1\", \"pages\": [\"=GAA+A1&EFS1/60\"]}.")]
    public static async Task<CallToolResult> ConnectionGenerateAsync(
        WritePipeline pipeline,
        IBridgeClient bridge,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("1-50 full page names")] string[] pages,
        [Description("Only count the connections now")] bool dry_run = false,
        [Description(PageDisplay.Description)] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The page names are checked by the Bridge, all at once (connection.generate.plan).
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (pages is null || pages.Length is < 1 or > PageWriteTools.MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {PageWriteTools.MaxPages} full page names.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new ConnectionGenerateArgs { Project = project, Pages = [.. pages!.Select(p => p ?? string.Empty)] };
        var request = new WriteRequest<ConnectionGenerateArgs, ConnectionGenerateArgs>(
            GenerateTool,
            ToolTier.Write,
            OperationNames.ConnectionGeneratePlan,
            OperationNames.ConnectionGenerateExecute,
            args,
            plan => new ConnectionGenerateArgs { Project = args.Project, Pages = args.Pages, Fingerprint = plan.Fingerprint },
            dry_run,
            PageWriteTools.Limit(args.Pages.Count),
            "Generating connections");
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, args.Project, args.Pages[0], cancellationToken).ConfigureAwait(false);
        WriteOutcome<ConnectionGenerateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ConnectionGenerateArgs, ConnectionGenerateArgs, ConnectionGenerateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return display.Apply(ToolResults.Error(ex.Error));
        }

        return display.Apply(outcome.Data is { } data ? Generated(data) : Planned(outcome, dry_run));
    }

    /// <summary>A commit that answered in time: the pages as read back, at most 100 connections listed (tool spec 3).</summary>
    private static CallToolResult Generated(ConnectionGenerateData data)
    {
        int added = data.Pages.Sum(p => p.AddedCount);
        int removed = data.Pages.Sum(p => p.RemovedCount);
        int listed = data.Pages.Sum(p => p.Added.Count + p.Removed.Count);
        var result = new ConnectionGenerateResult
        {
            Project = ResultShaper.CleanText(data.Project),
            // Nothing added or removed is no change (ADR-0010 section 1); EPLAN then keeps no undo step (live check, A19).
            Execution = added + removed == 0 ? ExecutionState.NotStarted : ExecutionState.Committed,
            Pages =
            [
                .. data.Pages.Select(p => new ConnectionGenerateResult.PageEntry
                {
                    Page = ResultShaper.CleanText(p.Page),
                    Before = p.Before,
                    After = p.After,
                    Added = [.. p.Added.Select(Connection)],
                    Removed = [.. p.Removed.Select(Connection)],
                }),
            ],
            Undo = added + removed == 0 ? null : "AI: " + GenerateTool,
            Truncated = listed < added + removed,
        };
        string?[] hints =
        [
            ResultShaper.CleanText(data.Warning),
            added + removed == 0 ? "EPLAN added and removed no connections on these pages. If lines drawn there still do not show as connections, ask the engineer to check EPLAN's messages for these pages." : null,
            result.Truncated
                ? string.Create(CultureInfo.InvariantCulture, $"Listed {listed} of {added + removed} added and removed connections, the removed ones first; eplan_connection_list with 'page' shows all connections of a page.")
                : null,
        ];
        result.Hint = Join(hints);
        return ToolResults.Success(
            added + removed == 0
                ? $"Generated connections on {ToolResults.Count(result.Pages.Count, "page")}: nothing added or removed, so nothing was changed."
                : string.Create(CultureInfo.InvariantCulture, $"Generated connections on {ToolResults.Count(result.Pages.Count, "page")}: {added} added, {removed} removed; Ctrl+Z in EPLAN undoes this step."),
            result);
    }

    /// <summary>A dry run, or a commit whose response was lost: only the counts before, from the plan's changes.</summary>
    private static CallToolResult Planned(WriteOutcome<ConnectionGenerateData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        var result = new ConnectionGenerateResult
        {
            Project = ResultShaper.CleanText(outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Pages =
            [
                .. outcome.Plan.Changes.Select(c => new ConnectionGenerateResult.PageEntry
                {
                    Page = ResultShaper.CleanText(c.Name),
                    Before = int.TryParse(c.OldValue, NumberStyles.None, CultureInfo.InvariantCulture, out int before) ? before : 0,
                }),
            ],
            Undo = committed ? "AI: " + GenerateTool : null,
            Recovered = outcome.Recovered,
            Hint = committed
                ? "The response was lost, but EPLAN reported the connections as generated; check them with eplan_connection_list and 'page'."
                : "Which connections EPLAN adds or removes is known only after running, so 'after', 'added' and 'removed' are null.",
        };
        string pages = ToolResults.Count(result.Pages.Count, "page");
        return ToolResults.Success(
            committed
                ? $"Generated connections on {pages}; Ctrl+Z in EPLAN undoes this step."
                : $"Dry run: {pages} {(result.Pages.Count == 1 ? "has" : "have")} {ToolResults.Count(result.Pages.Sum(p => p.Before), "connection")} now; the new ones are known only after running. Nothing was changed.",
            result);
    }

    private static ConnectionGenerateResult.ConnectionEntry Connection(ChangedConnection connection) => new()
    {
        From = End(connection.From),
        To = End(connection.To),
    };

    private static ConnectionListResult.EndEntry? End(ConnectionEnd? end) =>
        end is null ? null : new ConnectionListResult.EndEntry { Device = ResultShaper.CleanText(end.Device), Pin = ResultShaper.CleanText(end.Pin) };

    private static string? Join(IEnumerable<string?> hints) => string.Join(" ", hints.OfType<string>()) is { Length: > 0 } text ? text : null;
}

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
/// The Destructive cable tools (docs/tools/eplan_cable_renumber.md), through <see cref="WritePipeline"/> at
/// <see cref="ToolTier.Destructive"/>: approved every time, in scratch too, and backed up first (ADR-0005, ADR-0013).
/// </summary>
[McpServerToolType]
internal sealed class CableDestructiveTools
{
    public const string RenumberTool = "eplan_cable_renumber";
    public const int MaxCables = 200;

    /// <summary>The execution limit, the backup included (tool spec, section 5; S21 numbered 78 cables in 0.3 s).</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = RenumberTool,
        Title = "Number cables in an EPLAN project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(CableRenumberResult))]
    [Description(
        "Lets EPLAN renumber the given cables or every cable of the project in a cable numbering scheme; the new tags are " +
        "known only afterwards. Always needs the engineer's approval, in scratch too; the project is backed up first; one " +
        "Ctrl+Z in EPLAN undoes it. Example: {\"project\": \"Line1\", \"scheme\": \"Standard\"}.")]
    public static async Task<CallToolResult> CableRenumberAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")] string project,
        [Description("E.g. Standard")] string scheme,
        [Description("1-200 tags (eplan_device_list, category Cable); default: all")] string[]? cables = null,
        [Description("true: number only tags with '?'")] bool keep_existing = false,
        int start = 1,
        int step = 1,
        [Description("Only return the plan")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            violations.Add("'scheme' is required: a cable numbering scheme such as Standard.");
        }

        if (cables is { Length: < 1 or > MaxCables })
        {
            violations.Add($"'cables' must hold 1 to {MaxCables} cable tags; omit it for every cable of the project.");
        }

        if (start < 1 || step < 1)
        {
            violations.Add("'start' and 'step' must be at least 1.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new CableRenumberArgs { Project = project, Cables = cables is null ? null : [.. cables.Select(c => c ?? string.Empty)], Scheme = scheme, KeepExisting = keep_existing, Start = start, Step = step };
        var request = new WriteRequest<CableRenumberArgs, CableRenumberArgs>(
            RenumberTool,
            ToolTier.Destructive,
            OperationNames.CableRenumberPlan,
            OperationNames.CableRenumberExecute,
            args,
            (plan, backup) => new CableRenumberArgs { Project = args.Project, Cables = args.Cables, Scheme = args.Scheme, KeepExisting = args.KeepExisting, Start = args.Start, Step = args.Step, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            Limit,
            "Renumbering cables");
        WriteOutcome<CableRenumberData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<CableRenumberArgs, CableRenumberArgs, CableRenumberData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Numbered(outcome, dry_run);
    }

    private static CallToolResult Numbered(WriteOutcome<CableRenumberData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;

        // A commit that renamed no cable is no change (ADR-0010 section 1), as for eplan_device_renumber (A20).
        bool unchanged = committed && !outcome.Recovered && outcome.Data is { ChangedCount: 0 };
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        CableRenumberData? data = outcome.Data;
        var scope = new CableRenumberResult.ScopeEntry
        {
            CableCount = Number(details.GetValueOrDefault("cableCount")),
            KeepExisting = details.GetValueOrDefault("keepExisting") == "true",
            Scheme = ResultShaper.CleanText(details.GetValueOrDefault("scheme")) ?? string.Empty,
            Start = Number(details.GetValueOrDefault("start")) ?? 1,
            Step = Number(details.GetValueOrDefault("step")) ?? 1,
            Cables = Number(details.GetValueOrDefault("cables")) ?? 0,
        };
        var result = new CableRenumberResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = unchanged ? ExecutionState.NotStarted : outcome.Execution,
            DryRun = dryRun,
            Scope = scope,
            Changed = data is null ? null : [.. data.Changed.Select(c => new CableRenumberResult.ChangeEntry { From = ResultShaper.CleanText(c.From), To = ResultShaper.CleanText(c.To) })],
            ChangedCount = data?.ChangedCount,
            Backup = committed ? outcome.Backup : null,
            Undo = committed && !unchanged ? "AI: " + RenumberTool : null,
            Recovered = outcome.Recovered,
            Truncated = data is not null && data.ChangedCount > data.Changed.Count,
        };
        result.Hint = Hint(outcome, scope);
        string where = scope.CableCount is { } count ? "of the " + ToolResults.Count(count, "given cable") : "in the project";
        string cables = ToolResults.Count(scope.Cables, "cable");
        string summary = unchanged
            ? "EPLAN renamed no cable, so nothing was changed. A backup was made first (see 'backup')."
            : committed
            ? $"Renumbered {ToolResults.Count(data?.ChangedCount ?? 0, "cable")}. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step."
            : scope.Cables == 0 ? "Nothing to number. Nothing was changed."
            : scope.KeepExisting ? $"Dry run: would number the {cables} with '?' {where}; the new tags are known only after running. Nothing was changed."
            : $"Dry run: would renumber {cables} {where}; the new tags are known only after running. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? Hint(WriteOutcome<CableRenumberData> outcome, CableRenumberResult.ScopeEntry scope)
    {
        CableRenumberData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Plan.Changes.Count == 0)
        {
            hints.Add(scope.KeepExisting
                ? "No cable in scope has '?' in its tag, so keeping the existing tags leaves nothing to number; set keep_existing to false to renumber them."
                : "There is no cable in scope to number.");
        }
        else if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the numbering as committed; check the cables with eplan_device_list and category Cable.");
        }
        else if (data is not null)
        {
            if (data.ChangedCount == 0)
            {
                hints.Add("EPLAN renamed no cable.");
            }
            else if (data.ChangedCount > data.Changed.Count)
            {
                hints.Add($"{ToolResults.Count(data.ChangedCount, "cable")} changed their tag; only the first {data.Changed.Count.ToString(CultureInfo.InvariantCulture)} are listed.");
            }
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
    }

    private static int? Number(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
}

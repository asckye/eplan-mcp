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
/// The Destructive terminal tools (docs/tools/eplan_terminal_renumber.md), through <see cref="WritePipeline"/> at
/// <see cref="ToolTier.Destructive"/>: approved every time, in scratch too, and backed up first (ADR-0005, ADR-0013).
/// </summary>
[McpServerToolType]
internal sealed class TerminalDestructiveTools
{
    public const string RenumberTool = "eplan_terminal_renumber";
    public const int MaxStrips = 50;

    /// <summary>The execution limit, the backup included (tool spec, section 5; S21 numbered the whole project in 31–43 s).</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = RenumberTool,
        Title = "Number terminals in an EPLAN project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TerminalRenumberResult))]
    [Description(
        "Lets EPLAN number terminals of strips or the whole project (up to a minute) in a numbering scheme, by default " +
        "only designations with '?'; the new names are known only afterwards. Always needs the " +
        "engineer's approval, in scratch too; the project is backed up first; one Ctrl+Z in EPLAN undoes it. " +
        "Example: {\"project\": \"Line1\", \"strips\": [\"+A1-XD1\"], \"scheme\": \"Numerisch\"}.")]
    public static async Task<CallToolResult> TerminalRenumberAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")] string project,
        [Description("E.g. Numerisch")] string scheme,
        [Description("1-50 names (eplan_terminal_strip_list); default: all")] string[]? strips = null,
        [Description("false: renumber all")] bool only_unnumbered = true,
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
            violations.Add("'scheme' is required: a terminal numbering scheme such as Numerisch.");
        }

        if (strips is { Length: < 1 or > MaxStrips })
        {
            violations.Add($"'strips' must hold 1 to {MaxStrips} terminal strip names; omit it for the whole project.");
        }

        if (start < 1 || step < 1)
        {
            violations.Add("'start' and 'step' must be at least 1.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new TerminalRenumberArgs { Project = project, Strips = strips is null ? null : [.. strips.Select(s => s ?? string.Empty)], Scheme = scheme, OnlyUnnumbered = only_unnumbered, Start = start, Step = step };
        var request = new WriteRequest<TerminalRenumberArgs, TerminalRenumberArgs>(
            RenumberTool,
            ToolTier.Destructive,
            OperationNames.TerminalRenumberPlan,
            OperationNames.TerminalRenumberExecute,
            args,
            (plan, backup) => new TerminalRenumberArgs { Project = args.Project, Strips = args.Strips, Scheme = args.Scheme, OnlyUnnumbered = args.OnlyUnnumbered, Start = args.Start, Step = args.Step, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            Limit,
            "Renumbering terminals");
        WriteOutcome<TerminalRenumberData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<TerminalRenumberArgs, TerminalRenumberArgs, TerminalRenumberData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Numbered(outcome, dry_run);
    }

    private static CallToolResult Numbered(WriteOutcome<TerminalRenumberData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;

        // A commit that renamed no terminal is no change (ADR-0010 section 1), as for eplan_device_renumber, whose
        // numbering then leaves no undo step (live check, A20): Ctrl+Z would undo the step before.
        bool unchanged = committed && !outcome.Recovered && outcome.Data is { ChangedCount: 0 };
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        TerminalRenumberData? data = outcome.Data;
        var scope = new TerminalRenumberResult.ScopeEntry
        {
            StripCount = Number(details.GetValueOrDefault("stripCount")),
            OnlyUnnumbered = details.GetValueOrDefault("onlyUnnumbered") != "false",
            Scheme = ResultShaper.CleanText(details.GetValueOrDefault("scheme")) ?? string.Empty,
            Start = Number(details.GetValueOrDefault("start")) ?? 1,
            Step = Number(details.GetValueOrDefault("step")) ?? 1,
            Terminals = Number(details.GetValueOrDefault("terminalCount")) ?? 0,
        };
        var result = new TerminalRenumberResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = unchanged ? ExecutionState.NotStarted : outcome.Execution,
            DryRun = dryRun,
            Scope = scope,
            Changed = data is null ? null : [.. data.Changed.Select(c => new TerminalRenumberResult.ChangeEntry { From = ResultShaper.CleanText(c.From), To = ResultShaper.CleanText(c.To) })],
            ChangedCount = data?.ChangedCount,
            StillUnnumbered = data is null ? null : [.. data.StillUnnumbered.Select(t => ResultShaper.CleanText(t))],
            Backup = committed ? outcome.Backup : null,
            Undo = committed && !unchanged ? "AI: " + RenumberTool : null,
            Recovered = outcome.Recovered,
            Truncated = data is not null && (data.ChangedCount > data.Changed.Count || data.StillUnnumberedCount > data.StillUnnumbered.Count),
        };
        result.Hint = Hint(outcome, scope);
        string where = scope.StripCount is { } count ? "on " + ToolResults.Count(count, "strip") : "in the project";
        string terminals = ToolResults.Count(scope.Terminals, "terminal");
        string summary = unchanged
            ? "EPLAN renamed no terminal, so nothing was changed. A backup was made first (see 'backup')."
            : committed
            ? $"Numbered {ToolResults.Count(data?.ChangedCount ?? 0, "terminal")}{(data is { StillUnnumberedCount: > 0 } ? $"; {data.StillUnnumberedCount.ToString(CultureInfo.InvariantCulture)} still {(data.StillUnnumberedCount == 1 ? "contains" : "contain")} '?'" : string.Empty)}. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step."
            : scope.Terminals == 0 ? "Nothing to number. Nothing was changed."
            : scope.OnlyUnnumbered ? $"Dry run: would number the {terminals} with '?' {where}; the new names are known only after running. Nothing was changed."
            : $"Dry run: would renumber all {terminals} {where}; the new names are known only after running. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? Hint(WriteOutcome<TerminalRenumberData> outcome, TerminalRenumberResult.ScopeEntry scope)
    {
        TerminalRenumberData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Plan.Changes.Count == 0)
        {
            hints.Add(scope.OnlyUnnumbered
                ? "No terminal in scope has '?' in its designation, so there is nothing to number; set only_unnumbered to false to renumber every terminal."
                : "There is no terminal in scope to number.");
        }
        else if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the numbering as committed; check the terminals with eplan_terminal_strip_get.");
        }
        else if (data is not null)
        {
            if (data.ChangedCount == 0)
            {
                hints.Add("EPLAN renamed no terminal.");
            }
            else if (data.ChangedCount > data.Changed.Count)
            {
                hints.Add($"{ToolResults.Count(data.ChangedCount, "terminal")} changed their name; only the first {data.Changed.Count.ToString(CultureInfo.InvariantCulture)} are listed.");
            }

            if (data.StillUnnumberedCount > 0)
            {
                hints.Add(data.StillUnnumberedCount == 1
                    ? "1 terminal still has '?' in its designation after numbering; check it in EPLAN."
                    : $"{data.StillUnnumberedCount.ToString(CultureInfo.InvariantCulture)} terminals still have '?' in their designation after numbering; check them in EPLAN.");
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

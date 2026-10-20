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
/// The Destructive device tools (docs/tools/eplan_device_renumber.md), through <see cref="WritePipeline"/> at
/// <see cref="ToolTier.Destructive"/>: approved every time, in scratch too, and backed up first (ADR-0005, ADR-0013).
/// </summary>
[McpServerToolType]
internal sealed class DeviceDestructiveTools
{
    public const string RenumberTool = "eplan_device_renumber";

    /// <summary>The execution limit, the backup included (tool spec, section 5).</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = RenumberTool,
        Title = "Number device tags in an EPLAN project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DeviceRenumberResult))]
    [Description(
        "Lets EPLAN number device tags on pages or the whole project, by default only tags with '?' (e.g. -?QA1 after a " +
        "macro); the new tags are known only afterwards. Always needs the engineer's approval, in scratch too; the project " +
        "is backed up first, and one Ctrl+Z in EPLAN undoes it. Example: {\"project\": \"Line1\", \"pages\": [\"=GAA+A1&EFS1/60\"]}.")]
    public static async Task<CallToolResult> DeviceRenumberAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")] string project,
        [Description("1-50 exact full names; default: whole project")] string[]? pages = null,
        [Description("false: renumber every tag")] bool only_unnumbered = true,
        [Description("Default: EPLAN's")] string? scheme = null,
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

        if (pages is { Length: < 1 or > PageWriteTools.MaxPages })
        {
            violations.Add($"'pages' must hold 1 to {PageWriteTools.MaxPages} full page names; omit it for the whole project.");
        }

        if (start < 1 || step < 1)
        {
            violations.Add("'start' and 'step' must be at least 1.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new DeviceRenumberArgs { Project = project, Pages = pages is null ? null : [.. pages.Select(p => p ?? string.Empty)], OnlyUnnumbered = only_unnumbered, Scheme = ToolArguments.Optional(scheme), Start = start, Step = step };
        var request = new WriteRequest<DeviceRenumberArgs, DeviceRenumberArgs>(
            RenumberTool,
            ToolTier.Destructive,
            OperationNames.DeviceRenumberPlan,
            OperationNames.DeviceRenumberExecute,
            args,
            (plan, backup) => new DeviceRenumberArgs { Project = args.Project, Pages = args.Pages, OnlyUnnumbered = args.OnlyUnnumbered, Scheme = args.Scheme, Start = args.Start, Step = args.Step, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            Limit,
            "Renumbering devices");
        WriteOutcome<DeviceRenumberData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<DeviceRenumberArgs, DeviceRenumberArgs, DeviceRenumberData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Numbered(outcome, dry_run);
    }

    private static CallToolResult Numbered(WriteOutcome<DeviceRenumberData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        DeviceRenumberData? data = outcome.Data;
        var scope = new DeviceRenumberResult.ScopeEntry
        {
            PageCount = Number(details.GetValueOrDefault("pageCount")),
            OnlyUnnumbered = details.GetValueOrDefault("onlyUnnumbered") != "false",
            Scheme = ToolArguments.Optional(details.GetValueOrDefault("scheme")),
            Start = Number(details.GetValueOrDefault("start")) ?? 1,
            Step = Number(details.GetValueOrDefault("step")) ?? 1,
            Devices = Number(details.GetValueOrDefault("deviceCount")) ?? 0,
        };
        var result = new DeviceRenumberResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Scope = scope,
            Changed = data is null ? null : [.. data.Changed.Select(c => new DeviceRenumberResult.ChangeEntry { From = ResultShaper.CleanText(c.From), To = ResultShaper.CleanText(c.To) })],
            ChangedCount = data?.ChangedCount,
            StillUnnumbered = data is null ? null : [.. data.StillUnnumbered.Select(t => ResultShaper.CleanText(t))],
            Backup = committed ? outcome.Backup : null,
            Undo = committed ? "AI: " + RenumberTool : null,
            Recovered = outcome.Recovered,
            Truncated = data is not null && (data.ChangedCount > data.Changed.Count || data.StillUnnumberedCount > data.StillUnnumbered.Count),
        };
        result.Hint = Hint(outcome, scope);
        string where = scope.PageCount is { } count ? "on " + ToolResults.Count(count, "page") : "in the project";
        string devices = ToolResults.Count(scope.Devices, "device");
        string summary = committed
            ? $"Numbered {ToolResults.Count(data?.ChangedCount ?? 0, "device tag")}{(data is { StillUnnumberedCount: > 0 } ? $"; {data.StillUnnumberedCount.ToString(CultureInfo.InvariantCulture)} still {(data.StillUnnumberedCount == 1 ? "contains" : "contain")} '?'" : string.Empty)}. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step."
            : scope.Devices == 0 ? "Nothing to number. Nothing was changed."
            : scope.OnlyUnnumbered ? $"Dry run: would number the {devices} with '?' {where}; the new tags are known only after running. Nothing was changed."
            : $"Dry run: would renumber all {devices} {where}; the new tags are known only after running. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? Hint(WriteOutcome<DeviceRenumberData> outcome, DeviceRenumberResult.ScopeEntry scope)
    {
        DeviceRenumberData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Plan.Changes.Count == 0)
        {
            hints.Add(scope.OnlyUnnumbered
                ? "No device tag in scope contains '?', so there is nothing to number; set only_unnumbered to false to renumber every tag."
                : "There is no device in scope to number.");
        }
        else if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the numbering as committed; check the tags with eplan_device_list.");
        }
        else if (data is not null)
        {
            if (data.ChangedCount == 0)
            {
                hints.Add("EPLAN changed no tag.");
            }
            else if (data.ChangedCount > data.Changed.Count)
            {
                hints.Add($"{ToolResults.Count(data.ChangedCount, "device")} changed their tag; only the first {data.Changed.Count.ToString(CultureInfo.InvariantCulture)} are listed.");
            }

            if (data.StillUnnumberedCount > 0)
            {
                hints.Add(data.StillUnnumberedCount == 1
                    ? "1 device tag still contains '?' after numbering; check it in EPLAN."
                    : $"{data.StillUnnumberedCount.ToString(CultureInfo.InvariantCulture)} device tags still contain '?' after numbering; check them in EPLAN.");
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

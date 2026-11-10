using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The Destructive connection tools (docs/tools/eplan_connection_number.md), through <see cref="WritePipeline"/> at
/// <see cref="ToolTier.Destructive"/>: approved every time, in scratch too, and backed up first (ADR-0005, ADR-0013).
/// </summary>
[McpServerToolType]
internal sealed class ConnectionDestructiveTools
{
    public const string NumberTool = "eplan_connection_number";

    /// <summary>The execution limit, the backup included (tool spec, section 5; S21 numbered 2060 connections in about 1 s).</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = NumberTool,
        Title = "Number the connections of an EPLAN project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ConnectionNumberResult))]
    [Description("Numbers project connections using a wire numbering scheme, keeping manual numbers by default. Omit/true dry_run previews; false executes with engineer approval and a project backup, including scratch. One Ctrl+Z undoes it. See usage.")]
    public static async Task<CallToolResult> ConnectionNumberAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")] string project,
        [Description("E.g. Standard, Potenzialorientiert")] string scheme,
        [Description("Existing numbers to replace")][AllowedValues("except_manual", "none", "all")] string overwrite = "except_manual",
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add(ToolArguments.ProjectRequired);
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            violations.Add("'scheme' is required: a wire numbering scheme such as Standard.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new ConnectionNumberArgs { Project = project, Scheme = scheme, Overwrite = overwrite };
        var request = new WriteRequest<ConnectionNumberArgs, ConnectionNumberArgs>(
            NumberTool,
            ToolTier.Destructive,
            OperationNames.ConnectionNumberPlan,
            OperationNames.ConnectionNumberExecute,
            args,
            (plan, backup) => new ConnectionNumberArgs { Project = args.Project, Scheme = args.Scheme, Overwrite = args.Overwrite, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            Limit,
            "Numbering connections");
        WriteOutcome<ConnectionNumberData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ConnectionNumberArgs, ConnectionNumberArgs, ConnectionNumberData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Numbered(outcome, dry_run);
    }

    private static CallToolResult Numbered(WriteOutcome<ConnectionNumberData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;

        // A commit that changed no wire number is no change (ADR-0010 section 1), as for eplan_device_renumber (A20).
        bool unchanged = committed && !outcome.Recovered && outcome.Data is { ChangedCount: 0 };
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        ConnectionNumberData? data = outcome.Data;
        var scope = new ConnectionNumberResult.ScopeEntry
        {
            Scheme = ResultShaper.CleanText(details.GetValueOrDefault("scheme")) ?? string.Empty,
            Overwrite = details.GetValueOrDefault("overwrite") ?? string.Empty,
            Connections = Number(details.GetValueOrDefault("connections")) ?? 0,
            DesignatedBefore = Number(details.GetValueOrDefault("designatedBefore")) ?? 0,
        };
        var result = new ConnectionNumberResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = unchanged ? ExecutionState.NotStarted : outcome.Execution,
            DryRun = dryRun,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Scope = scope,
            Changed = data is null ? null : [.. data.Changed.Select(c => new ConnectionNumberResult.ChangeEntry { From = ResultShaper.CleanText(c.From), To = ResultShaper.CleanText(c.To), Page = ResultShaper.CleanText(c.Page) })],
            ChangedCount = data?.ChangedCount,
            DesignatedAfter = data?.DesignatedAfter,
            Backup = committed ? outcome.Backup : null,
            Undo = committed && !unchanged ? "AI: " + NumberTool : null,
            Recovered = outcome.Recovered,
            Truncated = data is not null && data.ChangedCount > data.Changed.Count,
        };
        result.Hint = dryRun ? WriteToolDefaults.ExecutionHint + " " + Hint(outcome) : Hint(outcome);
        string summary = unchanged
            ? "EPLAN changed no wire number, so nothing was changed. A backup was made first (see 'backup')."
            : committed
            ? $"Numbered {ToolResults.Count(data?.ChangedCount ?? 0, "connection")}. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step."
            : scope.Connections == 0 ? "Nothing to number. Nothing was changed."
            : $"Dry run: would number the {ToolResults.Count(scope.Connections, "connection")} of the project ({scope.DesignatedBefore.ToString(CultureInfo.InvariantCulture)} with a wire number); the new wire numbers are known only after running. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? Hint(WriteOutcome<ConnectionNumberData> outcome)
    {
        ConnectionNumberData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Plan.Changes.Count == 0)
        {
            hints.Add("The project has no connections to number; eplan_connection_generate makes them from the lines drawn on a page.");
        }
        else if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the numbering as committed; ask the engineer to check the wire numbers in EPLAN.");
        }
        else if (data is not null)
        {
            if (data.ChangedCount == 0)
            {
                hints.Add("EPLAN changed no wire number.");
            }
            else if (data.ChangedCount > data.Changed.Count)
            {
                hints.Add($"{ToolResults.Count(data.ChangedCount, "connection")} changed; the first {data.Changed.Count.ToString(CultureInfo.InvariantCulture)} are listed.");
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

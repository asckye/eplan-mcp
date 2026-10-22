using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Automatic cable generation through the Destructive pipeline (tool spec 5; ADR-0013).</summary>
[McpServerToolType]
internal sealed class CableGenerateTools
{
    public const string GenerateTool = "eplan_cable_generate";
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(Name = GenerateTool, Title = "Generate automatic cables", ReadOnly = false, Destructive = true,
        Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(CableGenerateResult))]
    [Description("Generates or updates automatic cables using three project schemes, preserving existing names. Always needs the engineer's approval, in scratch too, and a project backup first. New cables and selection effects are known only afterwards; undo is unverified. Example: {\"project\":\"Line1\",\"creation_scheme\":\"Standard\",\"numbering_scheme\":\"Standard\",\"selection_scheme\":\"Standard\"}.")]
    public static async Task<CallToolResult> CableGenerateAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")][MinLength(1)] string project,
        [Description("Cable creation scheme")][MinLength(1)] string creation_scheme,
        [Description("New cable numbering scheme")][MinLength(1)] string numbering_scheme,
        [Description("Automatic cable selection scheme")][MinLength(1)] string selection_scheme,
        [Description("Only return the plan")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        foreach ((string key, string? value) in new[] { ("project", project), ("creation_scheme", creation_scheme), ("numbering_scheme", numbering_scheme), ("selection_scheme", selection_scheme) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                violations.Add($"'{key}' is required and must not be blank.");
            }
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new CableGenerateArgs { Project = project, CreationScheme = creation_scheme.Trim(), NumberingScheme = numbering_scheme.Trim(), SelectionScheme = selection_scheme.Trim() };
        var request = new WriteRequest<CableGenerateArgs, CableGenerateArgs>(
            GenerateTool, ToolTier.Destructive, OperationNames.CableGeneratePlan, OperationNames.CableGenerateExecute, args,
            (plan, backup) => new CableGenerateArgs { Project = args.Project, CreationScheme = args.CreationScheme, NumberingScheme = args.NumberingScheme, SelectionScheme = args.SelectionScheme, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run, Limit, "Generating automatic cables");
        try
        {
            WriteOutcome<CableGenerateData> outcome = await pipeline.RunAsync<CableGenerateArgs, CableGenerateArgs, CableGenerateData>(request, progress, cancellationToken).ConfigureAwait(false);
            return Generated(outcome, args, dry_run);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }
    }

    private static CallToolResult Generated(WriteOutcome<CableGenerateData> outcome, CableGenerateArgs args, bool dryRun)
    {
        CableGenerateData? data = outcome.Data;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        var result = new CableGenerateResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Scope = new()
            {
                CreationScheme = ResultShaper.CleanText(details.GetValueOrDefault("creationScheme") ?? args.CreationScheme!),
                NumberingScheme = ResultShaper.CleanText(details.GetValueOrDefault("numberingScheme") ?? args.NumberingScheme!),
                SelectionScheme = ResultShaper.CleanText(details.GetValueOrDefault("selectionScheme") ?? args.SelectionScheme!),
            },
            Before = data?.Before ?? JsonSerializer.Deserialize<CableGenerateCounts>(details.GetValueOrDefault("before") ?? "{}", HostJson.Options)!,
            After = data?.After,
            Changes = data is null ? null : [.. data.Changes.Take(200).Select(c => new CableGenerateResult.ChangeEntry { Kind = c.Kind, Name = ResultShaper.CleanText(c.Name) })],
            ChangedCount = data?.ChangedCount,
            Backup = outcome.Backup,
            Recovered = outcome.Recovered,
            EplanMessages = [.. (data?.EplanMessages ?? []).Take(20).Select(m => new MessageListResult.MessageEntry
            {
                Level = ResultShaper.CleanText(m.Level), Number = ResultShaper.CleanText(m.Number),
                Text = ResultShaper.CleanText(m.Text), Occurrences = m.Occurrences,
            })],
            Hint = string.Join(" ", new[]
            {
                outcome.Recovered ? "The response was lost, but EPLAN reported the commit. Read the current cables; do not repeat generation." : null,
                dryRun ? "New cables and selection results are known only after running. Nothing was changed." : "Undo after commit has not been verified.",
                ResultShaper.CleanText(data?.Hint), ResultShaper.CleanText(data?.Warning),
                data is null ? null : $"Changes: {ToolResults.Count(data.ChangedCount, "record")}; inspect omitted changes with eplan_device_list (category Cable) or eplan_cable_get, without repeating generation.",
                data?.EplanMessageCount > 0 ? $"EPLAN messages: {ToolResults.Count(data.EplanMessageCount, "message")}; the list is limited by count and size budget." : null,
            }.OfType<string>()),
        };
        ResultShaper.FitItems(result, result.EplanMessages);
        ResultShaper.FitItems(result, result.Changes);
        result.Truncated = data?.Truncated == true || result.Changes?.Count < data?.ChangedCount || result.EplanMessages.Count < data?.EplanMessageCount;
        return ToolResults.Success(outcome.Execution == ExecutionState.Committed
            ? "Generated or updated automatic cables with existing names preserved. A project backup was made first (see 'backup')."
            : "Dry run: automatic cable generation only; new cables and selection results are known only after running. Nothing was changed.", result);
    }
}

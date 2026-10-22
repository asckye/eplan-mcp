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
/// Removing languages from a project (docs/tools/eplan_language_remove.md), which deletes their translations: through
/// <see cref="WritePipeline"/> at <see cref="ToolTier.Destructive"/>, so the engineer approves every call, in scratch
/// too, and the project is backed up first (ADR-0005, ADR-0013). The Bridge writes the language settings back on
/// failure, which the rollback does not (api-notes A28).
/// </summary>
[McpServerToolType]
internal sealed class LanguageDestructiveTools
{
    public const string RemoveTool = "eplan_language_remove";

    /// <summary>The time limit of the execution, half of it for the backup; S23 removed one language in about 3 s.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = RemoveTool,
        Title = "Remove languages from an EPLAN project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LanguageRemoveResult))]
    [Description(
        "Removes translation languages from an EPLAN project with all their translations in its texts; EPLAN may change " +
        "the displayed languages too. Always needs the engineer's approval, in scratch too; the project is backed up " +
        "first. Example: {\"project\": \"Line1\", \"languages\": [\"sv_SE\"]}. Setting languages is eplan_language_set.")]
    public static async Task<CallToolResult> LanguageRemoveAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")] string project,
        [Description("1-10 codes, e.g. sv_SE")] string[] languages,
        [Description("Only return the plan")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        // The codes, and that each is a translation language and not the source language, are checked by the Bridge.
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (languages is null or { Length: < 1 or > LanguageWriteTools.MaxLanguages })
        {
            violations.Add($"'languages' must hold 1 to {LanguageWriteTools.MaxLanguages} language codes such as en_US.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new LanguageRemoveArgs { Project = project, Languages = [.. languages!.Select(code => code ?? string.Empty)] };
        var request = new WriteRequest<LanguageRemoveArgs, LanguageRemoveArgs>(
            RemoveTool,
            ToolTier.Destructive,
            OperationNames.LanguageRemovePlan,
            OperationNames.LanguageRemoveExecute,
            args,
            (plan, backup) => new LanguageRemoveArgs { Project = args.Project, Languages = args.Languages, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            Limit,
            "Removing languages");
        WriteOutcome<LanguageRemoveData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<LanguageRemoveArgs, LanguageRemoveArgs, LanguageRemoveData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Removed(outcome, dry_run);
    }

    /// <summary>The result: read back after a commit; from the plan for a dry run or a recovered commit.</summary>
    private static CallToolResult Removed(WriteOutcome<LanguageRemoveData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        LanguageRemoveData? data = outcome.Data;
        List<string> removed = data is null ? [.. outcome.Plan.Changes.Select(c => ResultShaper.CleanText(c.Name))] : [.. data.Removed.Select(code => ResultShaper.CleanText(code))];
        int texts = data?.TextsAffected ?? (int.TryParse(details.GetValueOrDefault("textsAffected"), NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 0);
        LanguageSetResult.LanguagesEntry before = data is null ? LanguageWriteTools.Entry(details, "translation", "display", "source") : LanguageWriteTools.Entry(data.Before);
        LanguageSetResult.LanguagesEntry? after = data is null ? null : LanguageWriteTools.Entry(data.After);
        var hints = new List<string>();
        if (after is not null && !after.Display.SequenceEqual(before.Display, StringComparer.Ordinal))
        {
            hints.Add($"EPLAN also changed the displayed languages to {string.Join(", ", after.Display)}; use eplan_language_set to change them back.");
        }

        if (committed)
        {
            hints.Add("Ctrl+Z in EPLAN may bring the translations back without the language settings; the backup has both.");
        }

        if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the languages as removed; eplan_language_set with dry_run reads the settings now.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        var result = new LanguageRemoveResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Removed = removed,
            TextsAffected = texts,
            Before = before,
            After = after,
            Backup = committed ? outcome.Backup : null,
            Undo = committed ? "AI: " + RemoveTool : null,
            Recovered = outcome.Recovered,
            Hint = hints.Count == 0 ? null : string.Join(" ", hints),
        };
        string what = removed.Count == 1
            ? $"{removed[0]} and its {ToolResults.Count(texts, "translation")}"
            : $"{string.Join(", ", removed)} and their translations in {ToolResults.Count(texts, "text")}";
        return ToolResults.Success(
            committed ? $"Removed {what}. A backup was made first (see 'backup')." : $"Dry run: would remove {what}. Nothing was changed.",
            result);
    }
}

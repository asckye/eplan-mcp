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
/// Translating a project with EPLAN's translation database (docs/tools/eplan_project_translate.md), through
/// <see cref="WritePipeline"/> at <see cref="ToolTier.Destructive"/>: approved every time, in scratch too, and backed up
/// first (ADR-0005, ADR-0013); it may change many texts at once.
/// </summary>
[McpServerToolType]
internal sealed class ProjectDestructiveTools
{
    public const string TranslateTool = "eplan_project_translate";

    /// <summary>The most pages one call may name (tool spec 2).</summary>
    public const int MaxPages = 200;

    /// <summary>The execution limit, the backup included (tool spec 5; S23 translated the sample project in under 3 s).</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = TranslateTool,
        Title = "Translate an EPLAN project",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ProjectTranslateResult))]
    [Description(
        "Translates the texts of an EPLAN project, or of some pages, from its source language into all its translation " +
        "languages with EPLAN's translation database. Needs a source language: set it with eplan_language_set. Always " +
        "needs the engineer's approval, in scratch too; the project is backed up first. Example: {\"project\": \"Line1\", " +
        "\"pages\": [\"=GAA+A1&EFS1/1\"]}.")]
    public static async Task<CallToolResult> ProjectTranslateAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("From eplan_project_list")] string project,
        [Description("1-200 full page names; default: all")] string[]? pages = null,
        [Description("Only return the plan")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        // The page names and the languages are checked by the Bridge, all at once.
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (pages is { Length: < 1 or > MaxPages })
        {
            violations.Add($"'pages' must hold 1 to {MaxPages} full page names; omit it for the whole project.");
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new ProjectTranslateArgs { Project = project, Pages = pages is null ? null : [.. pages.Select(page => page ?? string.Empty)] };
        var request = new WriteRequest<ProjectTranslateArgs, ProjectTranslateArgs>(
            TranslateTool,
            ToolTier.Destructive,
            OperationNames.ProjectTranslatePlan,
            OperationNames.ProjectTranslateExecute,
            args,
            (plan, backup) => new ProjectTranslateArgs { Project = args.Project, Pages = args.Pages, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            Limit,
            "Translating");
        WriteOutcome<ProjectTranslateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ProjectTranslateArgs, ProjectTranslateArgs, ProjectTranslateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Translated(outcome, dry_run);
    }

    private static CallToolResult Translated(WriteOutcome<ProjectTranslateData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;

        // A commit that changed no text is no change (ADR-0010 section 1), as for eplan_connection_number.
        bool unchanged = committed && !outcome.Recovered && outcome.Data is { TextsChanged: 0 };
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        ProjectTranslateData? data = outcome.Data;
        var result = new ProjectTranslateResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = unchanged ? ExecutionState.NotStarted : outcome.Execution,
            DryRun = dryRun,
            Source = ResultShaper.CleanText(data?.Source ?? details.GetValueOrDefault("source")) ?? string.Empty,
            Languages = data is null ? Lines(details.GetValueOrDefault("languages")) : ResultShaper.CleanTexts(data.Languages),
            TextsChecked = data?.TextsChecked ?? Number(details.GetValueOrDefault("textsChecked")),
            TextsMissing = Number(details.GetValueOrDefault("textsMissing")),
            TextsChanged = data?.TextsChanged,
            Changed = data is null ? null : [.. data.Changed.Select(Entry)],
            Truncated = data is not null && data.TextsChanged > data.Changed.Count,
            StillMissing = data?.StillMissing,
            Backup = committed ? outcome.Backup : null,
            Undo = committed && !unchanged ? "AI: " + TranslateTool : null,
            Recovered = outcome.Recovered,
        };
        result.Hint = Hint(outcome, result);
        string into = $"the project's {ToolResults.Count(result.Languages.Count, "language")}";
        string summary = unchanged
            ? "EPLAN changed no text, so nothing was changed. A backup was made first (see 'backup')."
            : committed && data is not null
            ? $"Translated {ToolResults.Count(data.TextsChanged, "text")} into {into}" +
                (data.StillMissing > 0 ? $"; {ToolResults.Count(data.StillMissing, "text")} still miss a language because the translation database has no entry for them" : string.Empty) +
                ". A backup was made first (see 'backup')."
            : committed ? "Translated. A backup was made first (see 'backup')."
            : outcome.Plan.Changes.Count == 0 ? "Nothing to translate: no text in the source language misses a translation language. Nothing was changed."
            : $"Dry run: would translate from {result.Source} into {into}; {result.TextsMissing.ToString(CultureInfo.InvariantCulture)} of {ToolResults.Count(result.TextsChecked, "text")} miss a translation language. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? Hint(WriteOutcome<ProjectTranslateData> outcome, ProjectTranslateResult result)
    {
        var hints = new List<string>();
        if (outcome.Plan.Changes.Count == 0)
        {
            hints.Add("Every text with a text in the source language has all the translation languages. To translate into another language, add it with eplan_language_set.");
        }
        else if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the translation as committed; ask the engineer to check the texts in EPLAN.");
        }

        if (result.Truncated)
        {
            hints.Add($"{ToolResults.Count(result.TextsChanged ?? 0, "text")} changed; the first {result.Changed!.Count.ToString(CultureInfo.InvariantCulture)} are listed.");
        }

        if (result.StillMissing > 0)
        {
            hints.Add("The translation database has no entry for the texts that still miss a language: export them with eplan_export_untranslated for the engineer to translate.");
        }

        if (ResultShaper.CleanText(outcome.Data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
    }

    private static ProjectTranslateResult.ChangeEntry Entry(TranslatedText text) => new()
    {
        Kind = text.Kind,
        Name = ResultShaper.CleanText(text.Name),
        Property = text.Property,
        MissingBefore = ResultShaper.CleanTexts(text.MissingBefore),
        MissingAfter = ResultShaper.CleanTexts(text.MissingAfter),
    };

    private static List<string> Lines(string? text) => text is { Length: > 0 } ? ResultShaper.CleanTexts(text.Split('\n')) : [];

    private static int Number(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
}

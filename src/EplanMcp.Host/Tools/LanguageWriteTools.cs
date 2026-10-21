using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Setting a project's languages (docs/tools/eplan_language_set.md), through <see cref="WritePipeline"/> like every
/// write. The languages are project settings: no undo step holds them and a SafetyPoint does not take them back
/// (api-notes A28), so the Bridge writes them back on failure and the result says how to restore them.
/// </summary>
[McpServerToolType]
internal sealed class LanguageWriteTools
{
    public const string SetTool = "eplan_language_set";

    /// <summary>The most languages one argument may hold (tool spec 2).</summary>
    public const int MaxLanguages = 10;

    /// <summary>The time limit of the execution: a few settings calls, each well under a second in S23.</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private const string Unset = "##_##";

    private const string RestoreHint = "Language settings are not undone by Ctrl+Z; call this tool again with the 'before' values to restore them.";

    [McpServerTool(
        Name = SetTool,
        Title = "Set the languages of an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LanguageSetResult))]
    [Description(
        "Sets which languages the pages of an EPLAN project show, in order, or adds translation languages; translates " +
        "nothing. Ctrl+Z does not undo it: call again with the 'before' values. Outside scratch it needs the engineer's " +
        "approval. Example: {\"project\": \"Line1\", \"display\": [\"en_US\", \"zh_CN\"]}. Removing a language is " +
        "eplan_language_remove.")]
    public static async Task<CallToolResult> LanguageSetAsync(
        WritePipeline pipeline,
        IProgress<ProgressNotificationValue> progress,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("1-10 codes to show, in order; [\"##_##\"] clears")] string[]? display = null,
        [Description("1-10 codes to add, e.g. en_US")] string[]? add = null,
        [Description("Only return the settings before and after")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        // The codes, and that the displayed ones are translation languages, are checked by the Bridge, all at once.
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (display is null && add is null)
        {
            violations.Add("Give 'display', 'add' or both.");
        }

        foreach ((string name, string[]? codes) in new[] { ("display", display), ("add", add) })
        {
            if (codes is { Length: < 1 or > MaxLanguages })
            {
                violations.Add($"'{name}' must hold 1 to {MaxLanguages} language codes such as en_US; omit it to leave it out.");
            }
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new LanguageSetArgs
        {
            Project = project,
            Display = display is null ? null : [.. display.Select(code => code ?? string.Empty)],
            Add = add is null ? null : [.. add.Select(code => code ?? string.Empty)],
        };
        var request = new WriteRequest<LanguageSetArgs, LanguageSetArgs>(
            SetTool,
            ToolTier.Write,
            OperationNames.LanguageSetPlan,
            OperationNames.LanguageSetExecute,
            args,
            plan => new LanguageSetArgs { Project = args.Project, Display = args.Display, Add = args.Add, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit,
            "Setting languages");
        WriteOutcome<LanguageSetData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<LanguageSetArgs, LanguageSetArgs, LanguageSetData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Shaped(outcome, args, dry_run);
    }

    /// <summary>
    /// The result: read back after a commit; from the plan's details for a dry run, a plan with nothing to change, or a
    /// recovered commit.
    /// </summary>
    private static CallToolResult Shaped(WriteOutcome<LanguageSetData> outcome, LanguageSetArgs args, bool dryRun)
    {
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        LanguageSetData? data = outcome.Data;
        LanguageSetResult.LanguagesEntry before = data is null ? Entry(details, "translationBefore", "displayBefore") : Entry(data.Before);
        LanguageSetResult.LanguagesEntry after = data is null ? Entry(details, "translationAfter", "displayAfter") : Entry(data.After);
        bool committed = outcome.Execution == ExecutionState.Committed;
        bool nothing = outcome.Plan.Changes.Count == 0;
        List<string> added = [.. after.Translation.Except(before.Translation, StringComparer.Ordinal)];
        var hints = new List<string>();
        if (nothing)
        {
            hints.Add("The project already has these language settings.");
        }
        else
        {
            hints.Add(RestoreHint);
            if (added.Count > 0)
            {
                hints.Add("Languages added with 'add' are taken out only by eplan_language_remove.");
            }
        }

        // Only a read-back can show it: the plan keeps the displayed languages that were not given as they are.
        if (data is not null && args.Display is null && !after.Display.SequenceEqual(before.Display, StringComparer.Ordinal))
        {
            hints.Add($"EPLAN also changed the displayed languages to {string.Join(", ", after.Display)}; give 'display' to set them.");
        }

        if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the change; 'after' is as planned, so call this tool with dry_run to read the settings now.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        var result = new LanguageSetResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Before = before,
            After = after,
            Recovered = outcome.Recovered,
            Hint = string.Join(" ", hints),
        };
        string change = Change(added, args.Display);
        string summary = nothing
            ? "Nothing to change: the project already has these languages."
            : committed
                ? char.ToUpperInvariant(change[0]) + change[1..] + "."
                : $"Dry run: would {change}. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>"add sv_SE to the translation languages and set the displayed languages to en_US, zh_CN", in the past tense once done.</summary>
    private static string Change(List<string> added, List<string>? display) => string.Join(
        " and ",
        new[]
        {
            added.Count == 0 ? null : $"added {string.Join(", ", added)} to the translation languages",
            display is null ? null : display is [Unset] ? "cleared the displayed languages" : $"set the displayed languages to {string.Join(", ", display)}",
        }.OfType<string>());

    internal static LanguageSetResult.LanguagesEntry Entry(ProjectLanguages languages) => new()
    {
        Translation = [.. languages.Translation.Select(code => ResultShaper.CleanText(code))],
        Display = [.. languages.Display.Select(code => ResultShaper.CleanText(code))],
    };

    internal static LanguageSetResult.LanguagesEntry Entry(Dictionary<string, string> details, string translation, string display) => new()
    {
        Translation = Lines(details, translation),
        Display = Lines(details, display),
    };

    private static List<string> Lines(Dictionary<string, string> details, string key) =>
        details.GetValueOrDefault(key) is { Length: > 0 } text ? [.. text.Split('\n').Select(code => ResultShaper.CleanText(code))] : [];
}

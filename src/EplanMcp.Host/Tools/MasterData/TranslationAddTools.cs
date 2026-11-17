using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The language toolset's keyword write (docs/tools/eplan_translation_add.md). It changes the translation
/// database every project shares, so it is system master data like <see cref="TranslationImportTools"/>: approval and a
/// backup, in scratch too (ADR-0019). Parameter names are snake_case because the SDK publishes them as written.
/// </summary>
[McpServerToolType]
internal sealed class TranslationAddTools
{
    public const string Tool = "eplan_translation_add";

    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    [McpServerTool(
        Name = Tool,
        Title = "Add keywords to EPLAN's translation database",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TranslationAddResult))]
    [Description(
        "Adds keywords to EPLAN's translation database, shared by every project, and fills languages a keyword has no " +
        "text in. Never rewrites an existing translation (reported as keptTranslations), deletes a keyword or adds a " +
        "language; those stay in EPLAN's translation dictionary. Omit/true previews; false needs the engineer's " +
        "approval, in scratch too, and backs the database up first (.zw7). eplan_translation_keyword reads first. " +
        "Example: {\"keywords\":[{\"source\":\"Motor protection switch\",\"translations\":{\"de_DE\":\"Motorschutzschalter\"}}]}.")]
    public static async Task<CallToolResult> TranslationAddAsync(
        WritePipeline pipeline,
        [Description("1-200 entries, each {source, translations}; see usage")] JsonElement[] keywords,
        [Description("The language every source text is written in, e.g. en_US")] string reference_language = "en_US",
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        var entries = new List<TranslationKeywordEntry>();
        for (int i = 0; i < keywords.Length; i++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"'keywords[{i}]'");
            try
            {
                // The Bridge checks the texts themselves, so the Host only rejects what it cannot even read.
                entries.Add(keywords[i].Deserialize<TranslationKeywordEntry>(HostJson.Options)
                    ?? throw new JsonException("null"));
            }
            catch (JsonException)
            {
                violations.Add(at + " must be an object: {\"source\": \"text\", \"translations\": {\"de_DE\": \"Text\"}}.");
            }
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new TranslationKeywordSetArgs { Keywords = entries, ReferenceLanguage = reference_language };
        var request = new WriteRequest<TranslationKeywordSetArgs, TranslationKeywordSetArgs>(
            Tool,
            ToolTier.Destructive,
            OperationNames.TranslationKeywordSetPlan,
            OperationNames.TranslationKeywordSetExecute,
            args,
            plan => new TranslationKeywordSetArgs
            {
                Keywords = args.Keywords,
                ReferenceLanguage = args.ReferenceLanguage,
                Fingerprint = plan.Fingerprint,
                Database = plan.MasterData?.Database,
            },
            dry_run,
            Limit,
            "Writing translation keywords");
        WriteOutcome<TranslationKeywordSetData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<TranslationKeywordSetArgs, TranslationKeywordSetArgs, TranslationKeywordSetData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        TranslationKeywordSetData? data = outcome.Data;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        var result = new TranslationAddResult
        {
            Database = ResultShaper.CleanText(data?.Database ?? outcome.Plan.MasterData?.Database) ?? string.Empty,
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. ResultShaper.CleanChanges(outcome.Plan.Changes)],
            Keywords = data?.Keywords ?? entries.Count,
            ReferenceLanguage = ResultShaper.CleanText(data?.ReferenceLanguage ?? details.GetValueOrDefault("referenceLanguage")) ?? string.Empty,
            KeptTranslations = [.. (details.GetValueOrDefault("keptTranslations") ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => ResultShaper.CleanText(line) ?? line)],
            Added = data?.Added,
            Filled = data?.Filled,
            Kept = data?.Kept,
            Backup = outcome.MasterDataBackup,
            AuditId = outcome.AuditId,
            Recovered = outcome.Recovered,
            Hint = dry_run ? WriteToolDefaults.ExecutionHint : null,
        };
        string what = string.Create(CultureInfo.InvariantCulture,
            $"{result.Keywords} keywords (by {result.ReferenceLanguage}) in the translation database ({result.Database})");
        string summary = dry_run
            ? string.Create(CultureInfo.InvariantCulture, $"Dry run: would write {what}: {result.Changes.Count} changes, {result.KeptTranslations.Count} existing translations left alone. Nothing was changed.")
            : data is null
            ? $"Wrote {what}; the response was recovered, so the added/filled/kept counts are unavailable. Backup: {result.Backup}"
            : string.Create(CultureInfo.InvariantCulture, $"Wrote {what}: {result.Added} added, {result.Filled} filled, {result.Kept} kept. Backup: {result.Backup}");
        return ToolResults.Success(summary, result);
    }
}

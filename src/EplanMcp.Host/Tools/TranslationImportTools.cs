using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>The language toolset's system master-data import, full profile only (ADR-0019).</summary>
[McpServerToolType]
internal sealed class TranslationImportTools
{
    public const string Tool = "eplan_translation_import";

    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    private static readonly string[] _text = [".txt"];

    [McpServerTool(
        Name = Tool,
        Title = "Import translations into EPLAN's translation database",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TranslationImportResult))]
    [Description(
        "Imports a UTF-16 tab-separated .txt file from the imports folder into EPLAN's translation database, shared " +
        "by every project. Adds entries and fills empty translations; existing translations are never rewritten. " +
        "Always needs the engineer's approval, in scratch too; a .zw7 backup is made first. Cannot be undone; restore " +
        "the backup to recover. Example: {\"file\":\"glossary.txt\",\"reference_language\":\"en_US\"}.")]
    public static async Task<CallToolResult> TranslationImportAsync(
        WritePipeline pipeline,
        ImportFiles files,
        [Description("Relative path below imports, .txt, UTF-16 with BOM, at most 50 MB")] string file,
        [Description("One language column used to match original texts, e.g. en_US")] string reference_language = "en_US",
        [Description("Only return the validated file and its preview")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (ImportFiles.Problem("file", file, _text) is { } problem)
        {
            return ToolResults.InvalidArguments([problem]);
        }

        ImportFile source;
        try
        {
            source = await files.ResolveAsync("file", file, PartMasterDataTools.MaxFileBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (ImportFileException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        var args = new TranslationImportArgs { File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256, ReferenceLanguage = reference_language };
        var request = new WriteRequest<TranslationImportArgs, TranslationImportArgs>(
            Tool,
            ToolTier.Destructive,
            OperationNames.TranslationImportPlan,
            OperationNames.TranslationImportExecute,
            args,
            plan => new TranslationImportArgs { File = args.File, FilePath = args.FilePath, Sha256 = args.Sha256, ReferenceLanguage = args.ReferenceLanguage, Fingerprint = plan.Fingerprint, Database = plan.MasterData?.Database },
            dry_run,
            Limit,
            "Importing translations");
        WriteOutcome<TranslationImportData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<TranslationImportArgs, TranslationImportArgs, TranslationImportData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        TranslationImportData? data = outcome.Data;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        var result = new TranslationImportResult
        {
            Database = ResultShaper.CleanText(data?.Database ?? outcome.Plan.MasterData?.Database) ?? string.Empty,
            Execution = outcome.Execution,
            DryRun = dry_run,
            Entries = data?.Entries ?? int.Parse(details.GetValueOrDefault("entries") ?? "0", CultureInfo.InvariantCulture),
            ReferenceLanguage = ResultShaper.CleanText(data?.ReferenceLanguage ?? details.GetValueOrDefault("referenceLanguage")) ?? string.Empty,
            Added = data?.Added,
            Filled = data?.Filled,
            Kept = data?.Kept,
            Backup = outcome.MasterDataBackup,
            AuditId = outcome.AuditId,
            Recovered = outcome.Recovered,
            Languages = dry_run ? (details.GetValueOrDefault("languages") ?? string.Empty).Split(", ", StringSplitOptions.RemoveEmptyEntries) : null,
            Preview = dry_run ? JsonSerializer.Deserialize<List<Dictionary<string, string>>>(details.GetValueOrDefault("preview") ?? "[]", HostJson.Options) : null,
        };
        string what = FormattableString.Invariant($"{result.Entries} entries (by {result.ReferenceLanguage}) into the translation database ({result.Database})");
        string summary = dry_run ? $"Dry run: would import {what}. Nothing was changed."
            : data is null ? $"Imported {what}; the response was recovered, so added/filled/kept counts are unavailable. Backup: {result.Backup}"
            : FormattableString.Invariant($"Imported {what}: {result.Added} added, {result.Filled} filled, {result.Kept} kept. Backup: {result.Backup}");
        return ToolResults.Success(summary, result);
    }
}

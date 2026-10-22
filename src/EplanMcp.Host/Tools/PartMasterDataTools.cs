using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The tools that write EPLAN's parts database (docs/tools/eplan_part_import.md, eplan_part_sync.md), system master
/// data shared by every project (ADR-0019): through <see cref="WritePipeline"/> at <see cref="ToolTier.Destructive"/>
/// with the database in the plan, so the engineer approves every call, in scratch too, and the database is backed up
/// first; eplan_part_sync changes and backs up its project as well. Nothing rolls the database back; its backup is the
/// way back.
/// </summary>
[McpServerToolType]
internal sealed class PartMasterDataTools
{
    public const string ImportTool = "eplan_part_import";

    public const string SyncTool = "eplan_part_sync";

    /// <summary>The largest file the tool imports (tool spec 2).</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>How long an import may run, half of it for the backup (tool spec 5; S24 r13: 0.2 s for one part).</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(300);

    private static readonly string[] _xml = [".xml"];

    [McpServerTool(
        Name = ImportTool,
        Title = "Import a parts file into EPLAN's parts database",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PartImportResult))]
    [Description(
        "Imports a parts XML file (as eplan_part_export writes it) from the 'imports' folder in EplanMcp's data folder " +
        "into the parts database EPLAN is configured with, which every project shares. Always needs the engineer's " +
        "approval; the database is backed up first, and an import cannot be undone, only restored from that backup. " +
        "Example: {\"file\": \"supplier/relays.xml\"}.")]
    public static async Task<CallToolResult> PartImportAsync(
        WritePipeline pipeline,
        ImportFiles files,
        [Description("Path below the imports folder, .xml")] string file,
        [Description("append_new: only parts the database lacks")][AllowedValues("append_new", "update_existing", "update_and_append", "replace_and_append")] string mode = "append_new",
        [Description("How a part counts as already there")][AllowedValues("part_number", "unique_id", "unique_id_or_part_number")] string match_by = "part_number",
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (ImportFiles.Problem("file", file, _xml) is { } problem)
        {
            return ToolResults.InvalidArguments([problem]);
        }

        ImportFile source;
        try
        {
            source = await files.ResolveAsync("file", file, MaxFileBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (ImportFileException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        var args = new PartImportArgs { File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256, Mode = mode, MatchBy = match_by };
        var request = new WriteRequest<PartImportArgs, PartImportArgs>(
            ImportTool,
            ToolTier.Destructive,
            OperationNames.PartImportPlan,
            OperationNames.PartImportExecute,
            args,
            plan => new PartImportArgs { File = args.File, FilePath = args.FilePath, Sha256 = args.Sha256, Mode = args.Mode, MatchBy = args.MatchBy, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit,
            "Importing parts");
        WriteOutcome<PartImportData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PartImportArgs, PartImportArgs, PartImportData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Imported(outcome, source, mode, match_by, dry_run);
    }

    /// <summary>The result: read back after a commit; from the plan for a dry run, a file with nothing to do, or a recovered commit.</summary>
    private static CallToolResult Imported(WriteOutcome<PartImportData> outcome, ImportFile source, string mode, string matchBy, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        PartImportData? data = outcome.Data;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        var result = new PartImportResult
        {
            Database = ResultShaper.CleanText(data?.Database ?? outcome.Plan.MasterData?.Database) ?? string.Empty,
            Execution = outcome.Execution,
            DryRun = dryRun,
            File = source.Path,
            Sha256 = source.Sha256,
            Bytes = source.Bytes,
            Added = Numbers(data?.Added, details, "added"),
            AddedCount = data?.AddedCount ?? Count(details, "addedCount"),
            Updated = Numbers(data?.Updated, details, "updated"),
            UpdatedCount = data?.UpdatedCount ?? Count(details, "updatedCount"),
            Skipped = Numbers(data?.Skipped, details, "skipped"),
            SkippedCount = data?.SkippedCount ?? Count(details, "skippedCount"),
            PartsBefore = data?.PartsBefore ?? Count(details, "partsBefore"),
            PartsAfter = data?.PartsAfter,
            Missing = [.. (data?.Missing ?? []).Select(n => ResultShaper.CleanText(n) ?? string.Empty)],
            Backup = committed ? outcome.MasterDataBackup : null,
            Recovered = outcome.Recovered,
        };
        result.Hint = Hint(result, data, matchBy, committed);
        string what = $"{ToolResults.Count(result.AddedCount, "new part")}" + (result.UpdatedCount > 0 ? $", {result.UpdatedCount.ToString(CultureInfo.InvariantCulture)} updated" : string.Empty);
        string left = result.SkippedCount == 0 ? string.Empty : $"; left {ToolResults.Count(result.SkippedCount, "part")} of the file unchanged";
        string summary = committed
            ? $"Imported {what} into the parts database ({result.Database}){left}. A backup was made first (see 'backup')."
            : result.AddedCount + result.UpdatedCount == 0
                ? $"Nothing to import: with {mode}, every part of the file ({result.SkippedCount.ToString(CultureInfo.InvariantCulture)}) stays as it is in the parts database ({result.Database}). Nothing was changed."
                : $"Dry run: would import {what} into the parts database ({result.Database}){left}. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    private static string? Hint(PartImportResult result, PartImportData? data, string matchBy, bool committed)
    {
        var hints = new List<string>();
        if (committed)
        {
            hints.Add("Every project using this parts database sees the change. An import cannot be undone, not even with Ctrl+Z; to take it back, the engineer restores 'backup'.");
        }

        if (data is not null && (result.Missing.Count > 0 || (data.PartsAfter is { } read && read != data.PartsExpected)))
        {
            string after = data.PartsAfter?.ToString(CultureInfo.InvariantCulture) ?? "?";
            hints.Add($"The read-back does not match the plan ({after} parts instead of {data.PartsExpected.ToString(CultureInfo.InvariantCulture)}; not found: {(result.Missing.Count == 0 ? "none" : string.Join(", ", result.Missing))}). Ask the engineer to check these parts in EPLAN's parts management.");
        }

        if (matchBy != "part_number")
        {
            hints.Add("The parts were sorted into added, updated and skipped by part number; with match_by " + matchBy + " EPLAN may decide otherwise, which the read-back shows.");
        }

        if (result.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the import as done; eplan_part_search finds the parts.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
    }

    [McpServerTool(
        Name = SyncTool,
        Title = "Add an EPLAN project's parts to the parts database",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PartSyncResult))]
    [Description(
        "Synchronizes the parts an EPLAN project stores into the parts database EPLAN is configured with, which every " +
        "project shares; EPLAN also stores the parts the project only references in the project. Always needs the " +
        "engineer's approval; the database and the project are backed up first, and the database's side cannot be " +
        "undone. Example: {\"project\": \"Line1\"}.")]
    public static async Task<CallToolResult> PartSyncAsync(
        WritePipeline pipeline,
        [Description("From eplan_project_list")] string project,
        [Description("append_new: only parts the database lacks")][AllowedValues("append_new", "overwrite_existing", "overwrite_and_append")] string mode = "append_new",
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return ToolResults.InvalidArguments(["'project' is required for writes: a name or path from eplan_project_list."]);
        }

        var args = new PartSyncArgs { Project = project, Mode = mode };
        var request = new WriteRequest<PartSyncArgs, PartSyncArgs>(
            SyncTool,
            ToolTier.Destructive,
            OperationNames.PartSyncPlan,
            OperationNames.PartSyncExecute,
            args,
            (plan, backup) => new PartSyncArgs { Project = args.Project, Mode = args.Mode, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            Limit,
            "Synchronizing parts");
        WriteOutcome<PartSyncData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<PartSyncArgs, PartSyncArgs, PartSyncData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        return Synchronized(outcome, dry_run);
    }

    /// <summary>The result: read back after a commit; from the plan for a dry run, a project with nothing to do, or a recovered commit.</summary>
    private static CallToolResult Synchronized(WriteOutcome<PartSyncData> outcome, bool dryRun)
    {
        bool committed = outcome.Execution == ExecutionState.Committed;
        PartSyncData? data = outcome.Data;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        var result = new PartSyncResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project) ?? string.Empty,
            Database = ResultShaper.CleanText(data?.Database ?? outcome.Plan.MasterData?.Database) ?? string.Empty,
            Execution = outcome.Execution,
            DryRun = dryRun,
            Added = Numbers(data?.Added, details, "added"),
            AddedCount = data?.AddedCount ?? Count(details, "addedCount"),
            Overwritten = Numbers(data?.Overwritten, details, "updated"),
            OverwrittenCount = data?.OverwrittenCount ?? Count(details, "updatedCount"),
            StoredInProject = Numbers(data?.StoredInProject, details, "stored"),
            StoredInProjectCount = data?.StoredInProjectCount ?? Count(details, "storedCount"),
            PartsBefore = data?.PartsBefore ?? Count(details, "partsBefore"),
            PartsAfter = data?.PartsAfter,
            ProjectPartsBefore = data?.ProjectPartsBefore ?? Count(details, "projectPartsBefore"),
            ProjectPartsAfter = data?.ProjectPartsAfter,
            Missing = [.. (data?.Missing ?? []).Select(n => ResultShaper.CleanText(n) ?? string.Empty)],
            NotStored = [.. (data?.NotStored ?? []).Select(n => ResultShaper.CleanText(n) ?? string.Empty)],
            Backup = committed ? outcome.MasterDataBackup : null,
            ProjectBackup = committed ? outcome.Backup : null,
            Undo = committed ? "AI: " + SyncTool : null,
            Recovered = outcome.Recovered,
        };
        result.Hint = SyncHint(result, data, committed);
        string what = $"{ToolResults.Count(result.AddedCount, "part")} of the project to the parts database ({result.Database})"
            + (result.OverwrittenCount > 0 ? $", overwriting {ToolResults.Count(result.OverwrittenCount, "part")} there" : string.Empty)
            + (result.StoredInProjectCount > 0 ? $"; the project {(committed ? "now also stores" : "would also store")} {ToolResults.Count(result.StoredInProjectCount, "part")} it referenced" : string.Empty);
        string summary = committed
            ? $"Added {what}. Both were backed up first (see 'backup' and 'projectBackup')."
            : outcome.Plan.Changes.Count == 0
                ? $"Nothing to synchronize: with {details.GetValueOrDefault("mode") ?? "this mode"}, the parts database ({result.Database}) and project {result.Project} stay as they are. Nothing was changed."
                : $"Dry run: would add {what}. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    private static string? SyncHint(PartSyncResult result, PartSyncData? data, bool committed)
    {
        var hints = new List<string>();
        if (committed)
        {
            hints.Add("Every project using this parts database sees the change. Ctrl+Z in EPLAN undoes only the project's side ('undo'); the parts database comes back only from 'backup'.");
        }

        if (data is not null && (result.Missing.Count > 0 || result.NotStored.Count > 0 || (data.PartsAfter is { } read && read != data.PartsExpected)))
        {
            string after = data.PartsAfter?.ToString(CultureInfo.InvariantCulture) ?? "?";
            hints.Add($"The read-back does not match the plan ({after} parts in the database instead of {data.PartsExpected.ToString(CultureInfo.InvariantCulture)}; not in the database: {List(result.Missing)}; not stored in the project: {List(result.NotStored)}). Ask the engineer to check them in EPLAN.");
        }

        if (result.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the synchronization as done; eplan_part_search and eplan_part_list show the parts.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
    }

    private static string List(List<string> numbers) => numbers.Count == 0 ? "none" : string.Join(", ", numbers);

    /// <summary>The part numbers as read back, or from the plan's details for a dry run; parts data, cleaned.</summary>
    private static List<string> Numbers(List<string>? read, Dictionary<string, string> details, string key) =>
        [.. (read ?? [.. (details.GetValueOrDefault(key) ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)]).Select(n => ResultShaper.CleanText(n) ?? string.Empty)];

    private static int Count(Dictionary<string, string> details, string key) =>
        int.TryParse(details.GetValueOrDefault(key), NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 0;
}

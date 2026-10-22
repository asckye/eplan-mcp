using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Exports;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The exports of a system database EPLAN is configured with, not of a project (docs/tools/eplan_part_export.md,
/// eplan_translation_export.md): Read tier, yet they write files, so readOnlyHint is false (ADR-0012); only into a new folder the Host creates for each
/// call (SEC-20), below a folder named for the database, through <see cref="ExportRunner"/>, as the project exports do.
/// </summary>
[McpServerToolType]
internal sealed class MasterDataExportTools
{
    public const string PartTool = "eplan_part_export";
    public const int MaxParts = 500;

    /// <summary>The folder below the export root for the parts database's exports, in place of a project's (tool spec 5).</summary>
    public const string PartsFolder = "parts-database";

    /// <summary>The file's name when the call gives none (tool spec 2).</summary>
    public const string PartsFileName = "parts";

    /// <summary>EPLAN's master-data backup archive of a parts database (spike S24).</summary>
    public const string BackupExtension = ".zw6";

    public const string TranslationTool = "eplan_translation_export";

    /// <summary>The folder below the export root for the translation database's exports (eplan_translation_export.md, section 5).</summary>
    public const string TranslationsFolder = "translation-database";

    /// <summary>The file's name when the call gives none (eplan_translation_export.md, section 2).</summary>
    public const string TranslationsFileName = "translations";

    /// <summary>S24 exported the sample translation database in under a second (eplan_translation_export.md, section 5).</summary>
    public static readonly TimeSpan TranslationLimit = TimeSpan.FromSeconds(60);

    [McpServerTool(
        Name = PartTool,
        Title = "Export parts from EPLAN's parts database",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PartExportResult))]
    [Description(
        "Exports parts of EPLAN's parts database, which all projects share (not a project), as one file into a new " +
        "folder the server creates. Find the part numbers with eplan_part_search first. edz adds macros and pictures " +
        "but is slow (44 s, 128 MB for the sample); backup is EPLAN's restorable archive (.zw6) of the whole database. " +
        "Nothing is changed, no approval. Example: {\"parts\": [\"SIE.3RH2122-1HB40\"]}.")]
    public static async Task<CallToolResult> ExportPartsAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description("xml: part data; edz: with macros, pictures; backup: archive")][AllowedValues("xml", "edz", "backup")] string format = "xml",
        [Description("1-500 part numbers from eplan_part_search; default: all")] string[]? parts = null,
        [Description("File name only; .xml, .edz or .zw6 is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        string? extension = (format ?? "xml").Trim().ToLowerInvariant() switch
        {
            "xml" => ExportFileName.XmlExtension,
            "edz" => ExportFileName.EdzExtension,
            "backup" => BackupExtension,
            _ => null,
        };
        if (extension is null)
        {
            violations.Add("'format' must be xml, edz or backup.");
        }
        else if (extension == BackupExtension)
        {
            CheckBackup(parts, ToolArguments.Optional(file_name), violations);
        }

        if (parts is { Length: < 1 or > MaxParts })
        {
            violations.Add($"'parts' must hold 1 to {MaxParts.ToString(CultureInfo.InvariantCulture)} part numbers, such as [\"SIE.3RH2122-1HB40\"]; omit it for every part.");
        }

        string? fileName = ToolArguments.Optional(file_name);
        if (ExportFileName.Problem(fileName) is { } problem)
        {
            violations.Add("'file_name' " + problem);
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        // Blank and repeated part numbers, and whether the database has them, are the Bridge's to check (part.export.plan).
        var args = new ExportArgs { Format = extension == BackupExtension ? "backup" : extension![1..], Parts = parts is null ? null : [.. parts.Select(p => p ?? string.Empty)] };
        var job = new ExportJob(OperationNames.PartExportPlan, OperationNames.PartExportRun, args, "Exporting parts")
        {
            FileName = _ => ExportFileName.WithExtension(fileName, PartsFileName, extension),
            Folder = PartsFolder,
            SingleFile = extension,
            TimeoutHint = "EPLAN may still be busy for a while; export fewer parts per call with 'parts', or as xml rather than edz.",
        };
        return await ProjectExportTools.RunAsync(
            exports,
            job,
            progress,
            outcome => new PartExportResult { Database = ResultShaper.CleanText(outcome.Data.Database ?? string.Empty), PartCount = outcome.Data.PartCount ?? 0, VariantCount = outcome.Data.VariantCount ?? 0 },
            result => extension == BackupExtension
                ? $"Backed up the parts database to a {ByteSize.Format(result.TotalBytes)} EPLAN archive (.zw6) in a new folder; see 'directory'."
                : $"Exported {ToolResults.Count(result.PartCount, "part")} from the parts database to a {ByteSize.Format(result.TotalBytes)} {extension[1..].ToUpperInvariant()} file in a new folder; see 'directory'.",
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
        Name = TranslationTool,
        Title = "Export EPLAN's translation database",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TranslationExportResult))]
    [Description(
        "Exports EPLAN's whole translation database, which all projects share (not a project), as one file into a new " +
        "folder the server creates: tab-separated text or EPLAN's XML. The file can be a few MB: read its first lines " +
        "for the columns before reading on. The database is not changed and no approval is needed. Example: {}.")]
    public static async Task<CallToolResult> ExportTranslationsAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description("tab: tab-separated text; xml: EPLAN's XML")][AllowedValues("tab", "xml")] string format = "tab",
        [Description("File name only; .txt or .xml is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        string? extension = (format ?? "tab").Trim().ToLowerInvariant() switch
        {
            "tab" => ExportFileName.TextExtension,
            "xml" => ExportFileName.XmlExtension,
            _ => null,
        };
        if (extension is null)
        {
            violations.Add("'format' must be tab or xml.");
        }

        string? fileName = ToolArguments.Optional(file_name);
        if (ExportFileName.Problem(fileName) is { } problem)
        {
            violations.Add("'file_name' " + problem);
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        bool tab = extension == ExportFileName.TextExtension;
        var job = new ExportJob(OperationNames.TranslationExportPlan, OperationNames.TranslationExportRun, new ExportArgs { Format = tab ? "tab" : "xml" }, "Exporting translations")
        {
            FileName = _ => ExportFileName.WithExtension(fileName, TranslationsFileName, extension!),
            Folder = TranslationsFolder,
            SingleFile = extension,
            Limit = TranslationLimit,
            TimeoutHint = "EPLAN may still be busy for a while; ask the engineer to export the translation database in EPLAN instead.",
        };
        return await ProjectExportTools.RunAsync(
            exports,
            job,
            progress,
            outcome => new TranslationExportResult { Database = ResultShaper.CleanText(outcome.Data.Database ?? string.Empty) },
            result => $"Exported the translation database to a {ByteSize.Format(result.TotalBytes)} {(tab ? "tab-separated" : "XML")} file in a new folder; see 'directory'.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The backup archives the whole database, and its name becomes EPLAN's ARCHIVENAME, used only with ASCII letters,
    /// digits, - and _ so far (docs/tools/eplan_part_export.md, section 2); the Bridge checks both again.
    /// </summary>
    private static void CheckBackup(string[]? parts, string? fileName, List<string> violations)
    {
        if (parts is not null)
        {
            violations.Add("'parts' cannot be used with format backup: the backup archives the whole database.");
        }

        string name = ExportFileName.Stem(fileName, BackupExtension) ?? string.Empty;
        if (fileName is not null && (name.Length is 0 or > 64 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            violations.Add("'file_name' must use only ASCII letters, digits, - and _ (at most 64) for format backup.");
        }
    }
}

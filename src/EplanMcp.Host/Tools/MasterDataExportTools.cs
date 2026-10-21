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
/// The exports of a system database EPLAN is configured with, not of a project (docs/tools/eplan_part_export.md): Read
/// tier, yet they write files, so readOnlyHint is false (ADR-0012); only into a new folder the Host creates for each
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
        "but is slow (the sample database: 44 s, 128 MB): use it only for those. The database is not changed and no " +
        "approval is needed. Example: {\"parts\": [\"SIE.3RH2122-1HB40\"]}.")]
    public static async Task<CallToolResult> ExportPartsAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description("xml: part data only; edz: with macros and pictures")][AllowedValues("xml", "edz")] string format = "xml",
        [Description("1-500 part numbers from eplan_part_search; default: all")] string[]? parts = null,
        [Description("File name only; .xml or .edz is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        string? extension = (format ?? "xml").Trim().ToLowerInvariant() switch
        {
            "xml" => ExportFileName.XmlExtension,
            "edz" => ExportFileName.EdzExtension,
            _ => null,
        };
        if (extension is null)
        {
            violations.Add("'format' must be xml or edz.");
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
        var args = new ExportArgs { Format = extension![1..], Parts = parts is null ? null : [.. parts.Select(p => p ?? string.Empty)] };
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
            outcome => new PartExportResult { Database = ResultShaper.CleanText(outcome.Data.Database ?? string.Empty), PartCount = outcome.Data.PartCount ?? 0 },
            result => $"Exported {ToolResults.Count(result.PartCount, "part")} from the parts database to a {ByteSize.Format(result.TotalBytes)} {extension[1..].ToUpperInvariant()} file in a new folder; see 'directory'.",
            cancellationToken).ConfigureAwait(false);
    }
}

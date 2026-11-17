using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The export tools (docs/tools/eplan_export_pdf.md, eplan_export_dxf.md): Read tier, yet they write files, so
/// readOnlyHint is false (ADR-0012); only into a new folder the Host creates for each call (SEC-20), through
/// <see cref="ExportRunner"/>.
/// </summary>
[McpServerToolType]
internal sealed class ExportTools
{
    public const string PdfTool = "eplan_export_pdf";
    public const string DxfTool = "eplan_export_dxf";
    public const int MaxPages = 50;
    public const int MaxFiles = 50;

    [McpServerTool(
        Name = PdfTool,
        Title = "Export EPLAN pages as PDF",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ExportResult))]
    [Description(
        "Exports pages of an EPLAN project, or the whole project, as one PDF file into a new folder the server creates, " +
        "and returns that folder for the engineer. Writes files there only; the project is not changed and no approval " +
        "is needed. 300 pages take about 15 s. Example: {\"pages\": [\"=GAA+A1&EFS1/1\", \"=GAA+A1&EFS1/2\"], \"file_name\": \"Power\"}.")]
    public static async Task<CallToolResult> ExportPdfAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description(ToolArguments.ProjectDescription)] string? project = null,
        [Description("1-50 full page names; default: all")] string[]? pages = null,
        [Description("PDF scheme; default: the current one")] string? scheme = null,
        [AllowedValues("color", "grey", "black_white")] string color = "color",
        [Description("File name only; .pdf is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        CheckPages(pages, violations);
        string? fileName = ToolArguments.Optional(file_name);
        if (ExportFileName.Problem(fileName) is { } problem)
        {
            violations.Add("'file_name' " + problem);
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var job = new ExportJob(OperationNames.ExportPdfPlan, OperationNames.ExportPdfRun, Args(project, pages, scheme), "Exporting PDF")
        {
            FileName = plan => ExportFileName.Pdf(fileName, plan.Project),
        };
        job.Args.Color = color;
        return await RunAsync(exports, job, "PDF", progress, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
        Name = DxfTool,
        Title = "Export EPLAN pages as DXF or DWG",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ExportResult))]
    [Description(
        "Exports pages of an EPLAN project, or the whole project, as DXF or DWG files for other CAD software, one per " +
        "page, into a new folder the server creates; the scheme decides the format, subfolders and file names. Writes " +
        "files there only; the project is not changed and no approval is needed. Example: {\"pages\": [\"=GAA+A1&EFS1/1\"]}.")]
    public static async Task<CallToolResult> ExportDxfAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description(ToolArguments.ProjectDescription)] string? project = null,
        [Description("1-50 full page names; default: all")] string[]? pages = null,
        [Description("DXF/DWG scheme; default: the current one")] string? scheme = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        CheckPages(pages, violations);
        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var job = new ExportJob(OperationNames.ExportDxfPlan, OperationNames.ExportDxfRun, Args(project, pages, scheme), "Exporting DXF");
        return await RunAsync(exports, job, null, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Only the count: page names, repeats included, are the Bridge's to check, all at once (export.*.plan).</summary>
    private static void CheckPages(string[]? pages, List<string> violations)
    {
        if (pages is { Length: < 1 or > MaxPages })
        {
            violations.Add($"'pages' must hold 1 to {MaxPages} full page names; omit it for the whole project.");
        }
    }

    private static ExportArgs Args(string? project, string[]? pages, string? scheme) => new()
    {
        Project = ToolArguments.Optional(project),
        Pages = pages is null ? null : [.. pages.Select(p => p ?? string.Empty)],
        Scheme = ToolArguments.Optional(scheme),
    };

    /// <summary>
    /// The result of tool spec 3: at most <see cref="MaxFiles"/> files listed, the counts always of all. A null
    /// <paramref name="format"/> is read from the files: DWG when every file is one, DXF otherwise.
    /// </summary>
    private static async Task<CallToolResult> RunAsync(ExportRunner exports, ExportJob job, string? format, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken)
    {
        ExportOutcome outcome;
        try
        {
            outcome = await exports.RunAsync(job, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        var result = new ExportResult
        {
            Project = ResultShaper.CleanText(outcome.Data.Project),
            PageCount = outcome.Data.PageCount,
            Scheme = ResultShaper.CleanText(outcome.Data.Scheme),
            Directory = outcome.Directory,
            Files = [.. outcome.Files.Take(MaxFiles).Select(f => new ExportResult.FileEntry { Path = ResultShaper.CleanText(f.Path), Bytes = f.Bytes })],
            FileCount = outcome.Files.Count,
            TotalBytes = outcome.Files.Sum(f => f.Bytes),
        };
        result.Truncated = result.Files.Count < result.FileCount;
        result.Hint = Hint(ResultShaper.CleanText(outcome.Data.Warning), result.Truncated);
        if (!ResultShaper.Fits(result, ResultShaper.DefaultTokenBudget))
        {
            (result.Truncated, result.Hint) = (true, Hint(ResultShaper.CleanText(outcome.Data.Warning), cut: true));
            ResultShaper.FitItems(result, result.Files);
        }

        format ??= outcome.Files.All(f => f.Path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) ? "DWG" : "DXF";
        string summary = string.Format(
            CultureInfo.InvariantCulture,
            "Exported {0} to {1} ({2}) in a new folder; see 'directory'.",
            ToolResults.Count(result.PageCount, "page"),
            ToolResults.Count(result.FileCount, format + " file"),
            ByteSize.Format(result.TotalBytes));
        return ToolResults.Success(summary, result);
    }

    internal static string? Hint(string? warning, bool cut)
    {
        string? listed = cut ? "Not every file is listed in 'files'; all of them are in 'directory', and 'fileCount' counts them." : null;
        return warning is null ? listed : listed is null ? warning : warning + " " + listed;
    }
}

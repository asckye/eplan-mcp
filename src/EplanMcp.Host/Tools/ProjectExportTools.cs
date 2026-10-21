using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The exports of a whole project into one file (docs/tools/eplan_export_pxf.md, eplan_export_parts.md,
/// eplan_export_labels.md, eplan_export_untranslated.md): Read tier, yet they write files, so
/// readOnlyHint is false (ADR-0012); only into a new folder the Host creates for each call (SEC-20), through
/// <see cref="ExportRunner"/>, as eplan_export_pdf does.
/// </summary>
[McpServerToolType]
internal sealed class ProjectExportTools
{
    public const string PxfTool = "eplan_export_pxf";
    public const string PartsTool = "eplan_export_parts";
    public const string LabelsTool = "eplan_export_labels";
    public const string UntranslatedTool = "eplan_export_untranslated";
    public const int MaxLanguages = 10;

    /// <summary>The free space a PXF export needs first: S25 wrote 840 MB for the sample project with master data (tool spec 5).</summary>
    public const long PxfFreeBytes = 2L * 1024 * 1024 * 1024;

    private const string ProjectTimeoutHint = "EPLAN may still be busy for a while; ask the engineer to export this project in EPLAN instead.";

    [McpServerTool(
        Name = PxfTool,
        Title = "Export an EPLAN project as PXF",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PxfExportResult))]
    [Description(
        "Exports a whole EPLAN project, connections included, as one EPJ (PXF) file into a new folder the server " +
        "creates, to archive the project or send it on. The file can be several hundred MB and take over half a minute. " +
        "Writes files there only; the project is not changed and no approval is needed. Example: {\"file_name\": \"Line1 archive\"}.")]
    public static async Task<CallToolResult> ExportPxfAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description(ToolArguments.ProjectDescription)] string? project = null,
        [Description("Add the project's master data: larger, about twice as slow")] bool include_master_data = false,
        [Description("File name only; .epj is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        string? fileName = ExportFileName.Stem(ToolArguments.Optional(file_name), ExportFileName.PxfExtension);
        if (ExportFileName.Problem(fileName) is { } problem)
        {
            return ToolResults.InvalidArguments(["'file_name' " + problem]);
        }

        var args = new ExportArgs { Project = ToolArguments.Optional(project), IncludeMasterData = include_master_data };
        var job = new ExportJob(OperationNames.ExportPxfPlan, OperationNames.ExportPxfRun, args, "Exporting PXF")
        {
            FileName = plan => ExportFileName.Pxf(fileName, plan.Project),
            MinFreeBytes = PxfFreeBytes,
            SingleFile = ExportFileName.PxfExtension,
            TimeoutHint = ProjectTimeoutHint,
        };
        return await RunAsync(
            exports,
            job,
            progress,
            outcome => new PxfExportResult { Project = ResultShaper.CleanText(outcome.Data.Project), MasterData = include_master_data },
            result => $"Exported the project to a {ByteSize.Format(result.TotalBytes)} EPJ file in a new folder; see 'directory'.",
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
        Name = PartsTool,
        Title = "Export the parts list of an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PartsExportResult))]
    [Description(
        "Exports the parts list of an EPLAN project as one XML file into a new folder the server creates, for purchasing " +
        "or another program. The sample project takes about a minute and gives some 14 MB; to count parts, call " +
        "eplan_part_list instead. Writes files there only; the project is not changed and no approval is needed. " +
        "Example: {\"file_name\": \"Line1 parts\"}.")]
    public static async Task<CallToolResult> ExportPartsAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description(ToolArguments.ProjectDescription)] string? project = null,
        [Description("File name only; .xml is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        string? fileName = ToolArguments.Optional(file_name);
        if (ExportFileName.Problem(fileName) is { } problem)
        {
            return ToolResults.InvalidArguments(["'file_name' " + problem]);
        }

        var job = new ExportJob(OperationNames.ExportPartsPlan, OperationNames.ExportPartsRun, new ExportArgs { Project = ToolArguments.Optional(project) }, "Exporting the parts list")
        {
            FileName = plan => ExportFileName.WithExtension(fileName, plan.Project, ExportFileName.XmlExtension),
            TimeoutHint = ProjectTimeoutHint,
        };
        return await RunAsync(
            exports,
            job,
            progress,
            outcome => new PartsExportResult { Project = ResultShaper.CleanText(outcome.Data.Project) },
            result => $"Exported the parts list to a {ByteSize.Format(result.TotalBytes)} XML file in a new folder; see 'directory'.",
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
        Name = LabelsTool,
        Title = "Export EPLAN labels with a labelling scheme",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LabelsExportResult))]
    [Description(
        "Exports a list of an EPLAN project made by one of EPLAN's labelling schemes, such as every device tag or a " +
        "terminal diagram, as one text or Excel (.xlsx) file into a new folder the server creates. Writes files there " +
        "only; the project is not changed and no approval is needed. Example: {\"scheme\": \"Device tag list\"}.")]
    public static async Task<CallToolResult> ExportLabelsAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description("Labelling scheme, e.g. Device tag list, Terminal diagram")] string scheme,
        [Description(ToolArguments.ProjectDescription)] string? project = null,
        [Description("Text language, e.g. en_US; default: EPLAN UI")] string? language = null,
        [Description("File name only; the extension is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        string? schemeName = string.IsNullOrWhiteSpace(scheme) ? null : scheme.Trim();
        if (schemeName is null)
        {
            violations.Add("'scheme' is required: the name of a labelling scheme, such as 'Device tag list'.");
        }

        ToolArguments.CheckLanguage(language, violations);
        string? fileName = ToolArguments.Optional(file_name);
        if (ExportFileName.Problem(fileName) is { } problem)
        {
            violations.Add("'file_name' " + problem);
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        var args = new ExportArgs { Project = ToolArguments.Optional(project), Scheme = schemeName, Language = ToolArguments.Optional(language) };
        var job = new ExportJob(OperationNames.ExportLabelsPlan, OperationNames.ExportLabelsRun, args, "Exporting labels")
        {
            FileName = plan => ExportFileName.WithExtension(fileName, plan.Scheme ?? schemeName!, plan.Extension ?? string.Empty),
            TimeoutHint = ProjectTimeoutHint,
        };
        return await RunAsync(
            exports,
            job,
            progress,
            outcome => new LabelsExportResult { Project = ResultShaper.CleanText(outcome.Data.Project), Scheme = ResultShaper.CleanText(outcome.Data.Scheme) },
            result => $"Exported labels with scheme '{result.Scheme}' to a {ByteSize.Format(result.TotalBytes)} {(result.Files.All(f => f.Path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) ? "Excel" : "text")} file in a new folder; see 'directory'.",
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
        Name = UntranslatedTool,
        Title = "Export the untranslated texts of an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(UntranslatedExportResult))]
    [Description(
        "Exports the texts of an EPLAN project that EPLAN's translation database cannot translate into the given " +
        "languages, as one tab-separated Unicode text file into a new folder the server creates: a header row (source " +
        "language, each target language, COMMENT), then one text per row; read it to list them. Writes files there " +
        "only; the project is not changed and no approval is needed. Example: {\"languages\": [\"en_US\"]}.")]
    public static async Task<CallToolResult> ExportUntranslatedAsync(
        ExportRunner exports,
        IProgress<ProgressNotificationValue> progress,
        [Description("1-10 translation languages of the project, e.g. en_US")] string[] languages,
        [Description(ToolArguments.ProjectDescription)] string? project = null,
        [Description("File name only; .txt is added")] string? file_name = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (languages is not { Length: >= 1 and <= MaxLanguages })
        {
            violations.Add($"'languages' must hold 1 to {MaxLanguages.ToString(CultureInfo.InvariantCulture)} language codes, such as [\"en_US\"].");
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

        // Each code's shape, repeats and whether the project translates into it are the Bridge's to check, all at once.
        var args = new ExportArgs { Project = ToolArguments.Optional(project), Languages = [.. languages.Select(l => l ?? string.Empty)] };
        var job = new ExportJob(OperationNames.ExportUntranslatedPlan, OperationNames.ExportUntranslatedRun, args, "Exporting untranslated texts")
        {
            FileName = plan => ExportFileName.WithExtension(fileName, plan.Project, ExportFileName.TextExtension),
            TimeoutHint = ProjectTimeoutHint,
        };
        return await RunAsync(
            exports,
            job,
            progress,
            outcome => new UntranslatedExportResult
            {
                Project = ResultShaper.CleanText(outcome.Data.Project),
                Languages = ResultShaper.CleanTexts(outcome.Data.Languages ?? args.Languages),
            },
            result => $"Exported the untranslated texts for {string.Join(", ", result.Languages)} to a {ByteSize.Format(result.TotalBytes)} file in a new folder; see 'directory'.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the export and returns the tool's result, with the folder and its files filled in, or its error; the database exports use it too.</summary>
    internal static async Task<CallToolResult> RunAsync<TResult>(
        ExportRunner exports,
        ExportJob job,
        IProgress<ProgressNotificationValue> progress,
        Func<ExportOutcome, TResult> result,
        Func<TResult, string> summary,
        CancellationToken cancellationToken)
        where TResult : FileExportResult
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

        TResult data = result(outcome);
        data.Fill(outcome);
        return ToolResults.Success(summary(data), data);
    }
}

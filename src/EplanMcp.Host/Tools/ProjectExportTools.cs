using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The exports of a whole project into one file (docs/tools/eplan_export_pxf.md, eplan_export_parts.md): Read tier, yet they write files, so
/// readOnlyHint is false (ADR-0012); only into a new folder the Host creates for each call (SEC-20), through
/// <see cref="ExportRunner"/>, as eplan_export_pdf does.
/// </summary>
[McpServerToolType]
internal sealed class ProjectExportTools
{
    public const string PxfTool = "eplan_export_pxf";
    public const string PartsTool = "eplan_export_parts";

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

    /// <summary>Runs the export and returns the tool's result, with the folder and its files filled in, or its error.</summary>
    private static async Task<CallToolResult> RunAsync<TResult>(
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

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The import tools (docs/tools/eplan_import_dxf.md), through <see cref="WritePipeline"/> like every write. The Host
/// resolves the file below its import root (SEC-31) and reads its SHA-256; the Bridge checks the page and reads the
/// file again.
/// </summary>
[McpServerToolType]
internal sealed class ImportTools
{
    public const string DxfTool = "eplan_import_dxf";

    /// <summary>The largest drawing the tool imports (tool spec 2).</summary>
    public const long MaxDrawingBytes = 100L * 1024 * 1024;

    private static readonly string[] _drawings = [".dxf", ".dwg"];

    [McpServerTool(
        Name = DxfTool,
        Title = "Import a DXF or DWG drawing onto an EPLAN page",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImportDxfResult))]
    [Description(
        "Imports a DXF or DWG drawing from the 'imports' folder in EplanMcp's data folder onto an existing page, as " +
        "graphics only; one Ctrl+Z in EPLAN undoes it. Put the file there first; 'file' is its path below that folder. " +
        "Outside scratch it needs approval. Example: {\"project\": \"Line1\", \"page\": \"=GAA+A1&EFS1/92\", \"file\": \"drawings/4685200F.DXF\"}.")]
    public static async Task<CallToolResult> ImportDxfAsync(
        WritePipeline pipeline,
        ImportFiles files,
        [Description("From eplan_project_list")] string project,
        [Description("Full page name")] string page,
        [Description("Path below the imports folder")] string file,
        [Description("fit: to the page; none: own size")][AllowedValues("fit", "none")] string scale = "fit",
        [AllowedValues("center", "lower_left")] string position = "center",
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The page is the Bridge's to check (import.dxf.plan); the file is the Host's, as only it knows the import root.
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        if (ImportFiles.Problem("file", file, _drawings) is { } problem)
        {
            violations.Add(problem);
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        ImportFile source;
        try
        {
            source = await files.ResolveAsync("file", file, MaxDrawingBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (ImportFileException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        var args = new ImportDxfArgs { Project = project, Page = page ?? string.Empty, File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256, Scale = scale, Position = position };
        var request = new WriteRequest<ImportDxfArgs, ImportDxfArgs>(
            DxfTool,
            ToolTier.Write,
            OperationNames.ImportDxfPlan,
            OperationNames.ImportDxfExecute,
            args,
            plan => new ImportDxfArgs { Project = args.Project, Page = args.Page, File = args.File, FilePath = args.FilePath, Sha256 = args.Sha256, Scale = args.Scale, Position = args.Position, Fingerprint = plan.Fingerprint },
            dry_run,
            PageWriteTools.DefaultLimit,
            "Importing the drawing");
        WriteOutcome<ImportDxfData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ImportDxfArgs, ImportDxfArgs, ImportDxfData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        ImportDxfData? data = outcome.Data;
        var result = new ImportDxfResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Page = ResultShaper.CleanText(data?.Page ?? outcome.Plan.Changes.FirstOrDefault()?.Name ?? args.Page),
            File = source.Path,
            Sha256 = source.Sha256,
            Bytes = source.Bytes,
            ObjectsAdded = data?.ObjectsAdded,
            Undo = committed ? "AI: " + DxfTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the drawing as imported; check the page in EPLAN, or export it with eplan_export_image."
                : ResultShaper.CleanText(data?.Warning),
        };
        string summary = !committed ? "Dry run: would import one drawing. Nothing was changed."
            : data is null ? "Imported a drawing; Ctrl+Z in EPLAN undoes this step."
            : $"Imported a drawing with {ToolResults.Count(data.ObjectsAdded, "object")}; Ctrl+Z in EPLAN undoes this step.";
        return ToolResults.Success(summary, result);
    }
}

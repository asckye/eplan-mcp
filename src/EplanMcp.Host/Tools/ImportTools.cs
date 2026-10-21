using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The import tools (docs/tools/eplan_import_dxf.md, eplan_import_pxf.md), through <see cref="WritePipeline"/> like every
/// write. The Host resolves the file below its import root (SEC-31) and reads its SHA-256; the Bridge checks the page or
/// the new project's place and reads the file again where it can.
/// </summary>
[McpServerToolType]
internal sealed class ImportTools
{
    public const string DxfTool = "eplan_import_dxf";

    public const string PxfTool = "eplan_import_pxf";

    /// <summary>The largest drawing the tool imports (tool spec 2).</summary>
    public const long MaxDrawingBytes = 100L * 1024 * 1024;

    /// <summary>The largest EPJ file the tool imports (tool spec 2; S25: the sample project's has 598 MB).</summary>
    public const long MaxProjectBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>The longest name of a new project (tool spec 2).</summary>
    public const int MaxNameLength = 60;

    /// <summary>The folder of the first scratch root new projects go to (M4c design Q5).</summary>
    public const string ImportedFolderName = "imported";

    /// <summary>How long an EPJ import may run (tool spec 5; S22: 58 s for 598 MB).</summary>
    public static readonly TimeSpan PxfLimit = TimeSpan.FromSeconds(300);

    private static readonly string[] _drawings = [".dxf", ".dwg"];
    private static readonly string[] _projects = [".epj", ".pxf"];

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

    [McpServerTool(
        Name = PxfTool,
        Title = "Import an EPJ (PXF) file as a new EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ImportPxfResult))]
    [Description(
        "Imports an EPJ (PXF) file from the 'imports' folder in EplanMcp's data folder as a new project in the scratch " +
        "folder's 'imported' folder, without opening it; no other project changes. May take over a minute. 'file' is " +
        "its path below that folder. Example: {\"file\": \"incoming/Line1.epj\", \"name\": \"Line1_imported\"}.")]
    public static async Task<CallToolResult> ImportPxfAsync(
        WritePipeline pipeline,
        ImportFiles files,
        HostConfig config,
        [Description("Path below the imports folder")] string file,
        [Description("New project name")] string name,
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (ImportFiles.Problem("file", file, _projects) is { } problem)
        {
            violations.Add(problem);
        }

        if (ProjectNameProblem(name) is { } invalid)
        {
            violations.Add("'name' " + invalid);
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        ImportFile source;
        string folder;
        try
        {
            source = await files.ResolveAsync("file", file, MaxProjectBytes, cancellationToken).ConfigureAwait(false);
            folder = ImportedFolder(config);
        }
        catch (ImportFileException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        string project = name.Trim();
        var args = new ImportPxfArgs { File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256, Name = project, Target = Path.Combine(folder, project) };
        var request = new WriteRequest<ImportPxfArgs, ImportPxfArgs>(
            PxfTool,
            ToolTier.Write,
            OperationNames.ImportPxfPlan,
            OperationNames.ImportPxfExecute,
            args,
            plan => new ImportPxfArgs { File = args.File, FilePath = args.FilePath, Sha256 = args.Sha256, Name = args.Name, Target = args.Target, Fingerprint = plan.Fingerprint },
            dry_run,
            PxfLimit,
            "Importing the EPJ file");
        WriteOutcome<ImportPxfData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ImportPxfArgs, ImportPxfArgs, ImportPxfData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        var result = new ImportPxfResult
        {
            Execution = outcome.Execution,
            DryRun = dry_run,
            File = source.Path,
            Sha256 = source.Sha256,
            Bytes = source.Bytes,
            Project = new ImportPxfResult.ProjectEntry
            {
                Name = ResultShaper.CleanText(outcome.Data?.Name ?? outcome.Plan.Project),
                Path = ResultShaper.CleanText(outcome.Data?.Path ?? outcome.Plan.ProjectPath),
                Opened = false,
            },
            Seconds = outcome.Data?.Seconds,
            Recovered = outcome.Recovered,
            Hint = committed ? "The project is not opened; open it in EPLAN to work on it." : null,
        };
        return ToolResults.Success(
            committed ? "Imported the EPJ file as a new project in scratch; it is not opened yet." : "Dry run: would import the EPJ file as a new project in scratch. Nothing was changed.",
            result);
    }

    /// <summary>
    /// What is wrong with the new project's name (tool spec 2): a name only, as eplan_export_pdf's file_name, of 1 to
    /// <see cref="MaxNameLength"/> characters, and without a dot, so EPLAN cannot take part of it for an extension (S22
    /// tried only a name without one, api-notes A29); null when it is fine.
    /// </summary>
    internal static string? ProjectNameProblem(string? name)
    {
        string given = name?.Trim() ?? string.Empty;
        return given.Length == 0 ? "is required: the new project's name, such as Line1_imported."
            : given.Length > MaxNameLength ? $"must have at most {MaxNameLength} characters."
            : given.AsSpan().ContainsAny('\\', '/') ? "must be a name only, without '\\' or '/': the project always goes to the scratch folder's 'imported' folder."
            : given.Contains('.', StringComparison.Ordinal) ? "must not contain '.'; EPLAN adds the project's extensions itself."
            : ExportFileName.Problem(given);
    }

    /// <summary>
    /// The folder new projects go to (M4c design Q5): <c>imported</c> below the first scratch root, made when missing.
    /// It must not be a symbolic link or junction, so the project lands in scratch (SEC-05); the scratch root itself may
    /// be one.
    /// </summary>
    internal static string ImportedFolder(HostConfig config)
    {
        string root = config.ScratchRoots.Count > 0
            ? config.ScratchRoots[0]
            : throw new ImportFileException(ErrorReason.PolicyDenied, "No scratch folder is configured, so an EPJ file cannot be imported. Ask the engineer to check the scratch roots in config.json.", "name", string.Empty);
        string folder = Path.Combine(Path.GetFullPath(root), ImportedFolderName);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImportFileException(ErrorReason.EplanOperationFailed, "The scratch folder's 'imported' folder cannot be created. Ask the engineer to check the scratch folder.", "name", string.Empty);
        }

        return (new DirectoryInfo(folder).Attributes & FileAttributes.ReparsePoint) == 0
            ? folder
            : throw new ImportFileException(ErrorReason.PolicyDenied, "The scratch folder's 'imported' folder is a symbolic link or junction, which imports never write through. Ask the engineer to make it a plain folder.", "name", string.Empty);
    }
}

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Creates only new projects (docs/tools/eplan_project_create.md); the pipeline owns approval and outcomes.</summary>
[McpServerToolType]
internal sealed class ProjectCreateTools
{
    public const string CreateTool = "eplan_project_create";

    [McpServerTool(Name = CreateTool, Title = "Create project", ReadOnly = false, Destructive = false, Idempotent = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ProjectCreateResult))]
    [Description("Creates a closed project from an imports .zw9 basic project or .zw1 backup. " +
        "Target must not exist, even empty; default scratch/projects/name. Outside scratch needs " +
        "per-call approval (APPROVAL_DENIED). If an older basic project fails, upgrades a scratch copy and retries once. " +
        WriteToolDefaults.Description + " Example: {\"mode\":\"basic_project\",\"file\":\"basics/GB_bas001.zw9\",\"name\":\"Line1\",\"dry_run\":true}.")]
    public static async Task<CallToolResult> CreateAsync(
        WritePipeline pipeline, ImportFiles files, HostConfig config,
        [AllowedValues("basic_project", "backup")] string mode,
        [Description("Path below imports; at most 2 GB")][StringLength(260, MinimumLength = 1)] string file,
        [Description("New name, no path, dot or surrounding spaces")][StringLength(60, MinimumLength = 1)] string name,
        [Description("Full local path of a new folder; parent must exist")] string? destination = null,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (mode is not ("basic_project" or "backup"))
        {
            violations.Add("'mode' must be basic_project or backup.");
        }
        if (ImportFiles.Problem("file", file, [mode == "backup" ? ".zw1" : ".zw9"]) is { } problem)
        {
            violations.Add(problem);
        }
        if (ImportTools.ProjectNameProblem(name) is { } invalid)
        {
            violations.Add("'name' " + invalid.Split(':')[0]);
        }
        else if (name != name.Trim())
        {
            violations.Add("'name' must not have leading or trailing spaces.");
        }
        if (destination is not null && DestinationProblem(destination) is { } pathProblem)
        {
            violations.Add(pathProblem);
        }
        if (violations.Count > 0)
        {
            BridgeError error = ToolResults.InvalidArgumentError(violations);
            error.Execution = ExecutionState.NotStarted;
            return ToolResults.Error(error);
        }

        string? upgradeFolder = null;
        try
        {
            var clock = Stopwatch.StartNew();
            if (destination is null && config.ScratchRoots.Count == 0)
            {
                throw Failure(ErrorReason.PolicyDenied, "No scratch root is configured. Ask the engineer to check config.json.");
            }
            string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination ?? Path.Combine(config.ScratchRoots[0], "projects", name)));
            // Outside the scratch roots nothing is touched before approval, not even an existence check (WS-29): the
            // checks run in the argument factory below, after the engineer has seen the full path.
            if (InScratch(target, config))
            {
                CheckDestination(target, destination is not null, config);
            }
            ImportFile source = await files.ResolveAsync("file", file, ImportTools.MaxProjectBytes, cancellationToken).ConfigureAwait(false);
            var args = new NewProjectArgs { File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256, Name = name, Destination = target };
            clock.Stop(); // Resume in the normal argument factory after policy, approval and the started audit record.
            var request = new WriteRequest<NewProjectArgs, NewProjectArgs>(CreateTool, ToolTier.Write,
                mode == "backup" ? OperationNames.ProjectRestorePlan : OperationNames.ProjectCreatePlan,
                mode == "backup" ? OperationNames.ProjectRestoreExecute : OperationNames.ProjectCreateExecute, args,
                plan =>
                {
                    clock.Start();
                    CheckDestination(target, destination is not null, config);
                    RecheckSource(files, source);
                    upgradeFolder = mode == "basic_project" ? PrepareUpgradeFolder(config) : null;
                    return new NewProjectArgs
                    {
                        File = args.File,
                        FilePath = args.FilePath,
                        Sha256 = args.Sha256,
                        Name = name,
                        Destination = target,
                        Fingerprint = plan.Fingerprint,
                        UpgradeFolder = upgradeFolder,
                    };
                }, dry_run, TimeSpan.FromSeconds(mode == "backup" ? 180 : 120), mode == "backup" ? "Restoring the backup" : "Creating the project");
            WriteOutcome<NewProjectData> outcome = await pipeline.RunAsync<NewProjectArgs, NewProjectArgs, NewProjectData>(request, progress, cancellationToken).ConfigureAwait(false);
            clock.Stop();
            var result = new ProjectCreateResult
            {
                Execution = outcome.Execution,
                DryRun = dry_run,
                Fingerprint = outcome.Plan.Fingerprint,
                Mode = mode,
                File = ResultShaper.CleanText(request.Args.File!),
                Sha256 = request.Args.Sha256!,
                Project = new() { Name = ResultShaper.CleanText(name), Path = ResultShaper.CleanText(outcome.Plan.ProjectPath), Opened = false },
                Pages = outcome.Data?.Pages,
                ProjectGuid = ResultShaper.CleanText(outcome.Data?.ProjectGuid),
                DatabaseVersion = ResultShaper.CleanText(outcome.Data?.DatabaseVersion),
                Upgraded = outcome.Data?.Upgraded ?? false,
                Seconds = clock.Elapsed.TotalSeconds,
                Hint = dry_run ? "Compatibility will be checked during creation or restore. " + WriteToolDefaults.ExecutionHint : outcome.Recovered
                    ? "The new project is closed. The commit was recovered after a lost response; project metadata is unavailable." : "The new project is closed.",
                Changes = dry_run ? [.. outcome.Plan.Changes.Select(c => new PlannedChange { Action = c.Action, Kind = c.Kind, Name = ResultShaper.CleanText(c.Name), NewValue = ResultShaper.CleanText(c.NewValue) })] : null,
            };
            if (result.Upgraded)
            {
                result.Hint += " EPLAN upgraded a copy of the basic project from an older version; the original file is unchanged.";
            }
            string summary = mode == "backup" ? $"Restored backup as new project '{result.Project.Name}'; it is closed."
                : $"Created project '{result.Project.Name}' from a basic project; it is closed.";
            return ToolResults.Success(dry_run ? "Dry run: would create a closed new project. Nothing was changed." : summary, result);
        }
        catch (ImportFileException ex)
        {
            return ToolResults.Error(ex.Error);
        }
        catch (BridgeCallException ex)
        {
            if (ex.Error.Execution == ExecutionState.NotStarted && upgradeFolder is { Length: <= 80 })
            {
                try
                {
                    if (Directory.Exists(upgradeFolder))
                    {
                        Directory.Delete(upgradeFolder);
                    }
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    ex.Error.Metadata ??= [];
                    ex.Error.Metadata["warning"] = "The unused basic-project upgrade folder could not be deleted.";
                }
            }
            return ToolResults.Error(ex.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeError error = ToolResults.InvalidArgumentError(["The source or destination cannot be accessed. Ask the engineer to check the folders."]);
            error.Execution = ExecutionState.NotStarted;
            return ToolResults.Error(error);
        }
    }

    internal static string? DestinationProblem(string path) => !Path.IsPathFullyQualified(path) || path.StartsWith('\\') || path.Length <= 3
        ? "'destination' must be the full local path of a new folder, without UNC or device paths."
        : ImportFiles.PathProblem("destination", path[3..].TrimEnd('\\', '/'), "a local drive", "projects/Line1");

    internal static string? PrepareUpgradeFolder(HostConfig config)
    {
        string? root = config.ScratchRoots
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .OrderBy(path => path.Length)
            .FirstOrDefault();
        if (root is null)
        {
            return null;
        }

        string folder = Path.Combine(root, ".up", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3)));
        if (folder.Length <= 80)
        {
            try
            {
                CheckDestination(folder, explicitParent: false, config);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeCallException)
            {
                return null;
            }
        }
        return folder;
    }

    internal static bool InScratch(string target, HostConfig config) => config.ScratchRoots.Any(root =>
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    });

    private static void CheckDestination(string target, bool explicitParent, HostConfig config)
    {
        if (Path.Exists(target) || new FileInfo(target).LinkTarget is not null || new DirectoryInfo(target).LinkTarget is not null)
        {
            throw Failure(ErrorReason.AlreadyExists, "Choose a new destination; existing folders and projects are never reused.");
        }
        string? parent = Path.GetDirectoryName(target);
        if (explicitParent && !Directory.Exists(parent))
        {
            throw Failure(ErrorReason.InvalidArgument, "Invalid arguments: 'destination' parent must exist.");
        }
        for (string? part = parent; part is not null; part = Path.GetDirectoryName(part))
        {
            if (config.ScratchRoots.Any(r => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)), part, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
            if (Path.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
            {
                throw Failure(ErrorReason.InvalidArgument, "Invalid arguments: 'destination' leads through a link or junction.");
            }
        }
    }

    private static void RecheckSource(ImportFiles files, ImportFile source)
    {
        try
        {
            var info = ImportFiles.Walk("file", files.Root, source.Path, ImportFiles.RootName, folder: false) as FileInfo;
            if (info is null || info.Length != source.Bytes || info.LastWriteTimeUtc != source.LastWriteUtc)
            {
                throw new IOException("The source changed.");
            }
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != source.Sha256)
            {
                throw new IOException("The source changed.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImportFileException)
        {
            throw Failure(ErrorReason.PlanStale, "The source file changed or cannot be read. Call again to make a new plan and approval.");
        }
    }

    private static BridgeCallException Failure(string reason, string message) => new(new BridgeError
    {
        Reason = reason,
        Message = message,
        Execution = ExecutionState.NotStarted,
        Metadata = reason == ErrorReason.InvalidArgument ? new() { ["violations"] = message } : null,
    });
}

using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace EplanMcp.Host.Exports;

/// <summary>One export as a tool states it: its Bridge operations, the plan's arguments, and how its progress reads.</summary>
internal sealed record ExportJob(string PlanOperation, string RunOperation, ExportArgs Args, string Label)
{
    /// <summary>PDF and the project exports: the file's name, from what the plan found (the project's name, a scheme's extension).</summary>
    public Func<ExportPlanData, string>? FileName { get; init; }

    /// <summary>The least free space the export root's drive needs before EPLAN is called; 0 for no check (PXF, tool spec 5).</summary>
    public long MinFreeBytes { get; init; }

    /// <summary>The extension of the one file the export must leave, at the top of its folder; null when any files do (PXF, tool spec 5).</summary>
    public string? SingleFile { get; init; }

    /// <summary>What the model can do after a timeout.</summary>
    public string TimeoutHint { get; init; } = "Export fewer pages per call with 'pages'; EPLAN may still be busy for a while.";
}

/// <summary>What an export left: the Bridge's data, the new folder, and every file in it, sorted by path.</summary>
internal sealed record ExportOutcome(ExportRunData Data, string Directory, IReadOnlyList<ExportFile> Files);

/// <summary>
/// The steps every export tool goes through (docs/tools/eplan_export_pdf.md, section 5; SEC-20): the Bridge plans,
/// the Host creates a new folder, the Bridge exports into it, the Host checks what is there. After an error, a timeout
/// or a cancel the folder is deleted and nothing partial is returned (tool spec 4). A read: no approval, no audit.
/// </summary>
internal sealed partial class ExportRunner(IBridgeClient bridge, ExportFolders folders, TimeProvider clock, ILogger<ExportRunner> logger)
{
    /// <summary>The longest an export may take (M4 design 4.1).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Runs <paramref name="job"/>. Throws <see cref="BridgeCallException"/> with the tool's error, and
    /// <see cref="OperationCanceledException"/> when the client cancelled. <paramref name="progress"/> gets the wait for
    /// EPLAN as write tools report it (<see cref="BridgeProgress"/>): the Bridge's steps and a heartbeat.
    /// </summary>
    public async Task<ExportOutcome> RunAsync(ExportJob job, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        ExportPlanData plan = await bridge.InvokeAsync<ExportArgs, ExportPlanData>(job.PlanOperation, job.Args, cancellationToken).ConfigureAwait(false);
        if (job.MinFreeBytes > 0 && folders.FreeBytes() is { } free && free < job.MinFreeBytes)
        {
            throw Failed($"Only {ByteSize.Format(free)} is free on the drive of the server's export folder, less than the {ByteSize.Format(job.MinFreeBytes)} this export needs, so nothing was exported. Ask the engineer to clean up the server's exports folder; 'eplanmcp doctor' reports its size.");
        }

        ExportFolder folder;
        try
        {
            folder = folders.Create(plan.Project);
        }
        catch (ExportFolderException ex)
        {
            LogFolderFailed(ex);
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.Internal,
                Message = "The server could not create a new folder for the export in its data directory, so nothing was exported. Ask the engineer to run 'eplanmcp doctor'.",
            });
        }

        bool kept = false;
        try
        {
            ExportArgs args = RunArgs(job, folder.Path, plan);
            ExportRunData data;
            using (BridgeProgress? waiting = BridgeProgress.Start(CallProgress.For(progress), job.Label, clock, cancellationToken))
            {
                data = await bridge.InvokeAsync<ExportArgs, ExportRunData>(job.RunOperation, args, Timeout, waiting, cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<ExportFile> files = Files(folder, job.SingleFile);
            kept = true;
            var outcome = new ExportOutcome(data, folder.Path, files);
            LogExported(job.RunOperation, files.Count);
            return outcome;
        }
        catch (BridgeCallException ex) when (ex.Error.Reason == ErrorReason.Timeout)
        {
            string seconds = Timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.Timeout,
                Message = $"The export did not finish within {seconds} s, so no files were kept. {job.TimeoutHint}",
                Retryable = true,
            });
        }
        finally
        {
            if (!kept && !folder.TryDelete())
            {
                LogNotDeleted(folder.Path);
            }
        }
    }

    private static ExportArgs RunArgs(ExportJob job, string directory, ExportPlanData plan) => new()
    {
        Project = job.Args.Project,
        Pages = job.Args.Pages,
        Scheme = job.Args.Scheme,
        Color = job.Args.Color,
        Format = job.Args.Format,
        WidthPx = job.Args.WidthPx,
        IncludeMasterData = job.Args.IncludeMasterData,
        Language = job.Args.Language,
        Directory = directory,
        FileName = job.FileName?.Invoke(plan),
    };

    /// <summary>
    /// The folder's files; a link, a path outside it or no file at all fails the export (SEC-20), and so does anything
    /// but one <paramref name="single"/> file at the top when the job asks for that. Empty folders are not files: PXF
    /// leaves one named like its file (S25), which must stay empty.
    /// </summary>
    private IReadOnlyList<ExportFile> Files(ExportFolder folder, string? single)
    {
        IReadOnlyList<ExportFile> files;
        try
        {
            files = folder.Files();
        }
        catch (ExportFolderException ex)
        {
            LogFolderRefused(ex);
            throw Failed("The export left a link, a junction or a path outside its folder, so it counts as failed and no files were kept. Ask the engineer to check the export scheme in EPLAN.");
        }

        if (files.Count == 0)
        {
            throw Failed("EPLAN reported the export as done but wrote no file, so no folder was kept. Ask the engineer to check the export scheme in EPLAN.");
        }

        return single is null || (files.Count == 1 && !files[0].Path.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) && files[0].Path.EndsWith(single, StringComparison.OrdinalIgnoreCase))
            ? files
            : throw Failed($"EPLAN left other files than one {single} file in the export's folder, so it counts as failed and no files were kept. Ask the engineer to export it in EPLAN.");
    }

    private static BridgeCallException Failed(string message) => new(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = message });

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create an export folder")]
    private partial void LogFolderFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Export refused: its folder holds what an export must not leave")]
    private partial void LogFolderRefused(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete the folder of a failed export: {Folder}")]
    private partial void LogNotDeleted(string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Operation} left {Files} files")]
    private partial void LogExported(string operation, int files);
}

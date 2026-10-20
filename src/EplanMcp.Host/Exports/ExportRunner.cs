using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace EplanMcp.Host.Exports;

/// <summary>One export as a tool states it: its Bridge operations, the plan's arguments, and how its heartbeat reads.</summary>
internal sealed record ExportJob(string PlanOperation, string RunOperation, ExportArgs Args, string Label)
{
    /// <summary>PDF: the file's name, from the project's name the plan returned.</summary>
    public Func<string, string>? FileName { get; init; }
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
    /// <see cref="OperationCanceledException"/> when the client cancelled. <paramref name="progress"/> gets the heartbeat.
    /// </summary>
    public async Task<ExportOutcome> RunAsync(ExportJob job, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        ExportPlanData plan = await bridge.InvokeAsync<ExportArgs, ExportPlanData>(job.PlanOperation, job.Args, cancellationToken).ConfigureAwait(false);
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
            ExportArgs args = RunArgs(job, folder.Path, plan.Project);
            ExportRunData data;
            using (ExportHeartbeat.Start(progress, job.Label, clock))
            {
                data = await bridge.InvokeAsync<ExportArgs, ExportRunData>(job.RunOperation, args, Timeout, null, cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<ExportFile> files = Files(folder);
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
                Message = $"The export did not finish within {seconds} s, so no files were kept. Export fewer pages per call with 'pages'; EPLAN may still be busy for a while.",
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

    private static ExportArgs RunArgs(ExportJob job, string directory, string project) => new()
    {
        Project = job.Args.Project,
        Pages = job.Args.Pages,
        Scheme = job.Args.Scheme,
        Color = job.Args.Color,
        Format = job.Args.Format,
        WidthPx = job.Args.WidthPx,
        Directory = directory,
        FileName = job.FileName?.Invoke(project),
    };

    /// <summary>The folder's files; a link, a path outside it or no file at all fails the export (SEC-20).</summary>
    private IReadOnlyList<ExportFile> Files(ExportFolder folder)
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

        return files.Count > 0 ? files : throw Failed("EPLAN reported the export as done but wrote no file, so no folder was kept. Ask the engineer to check the export scheme in EPLAN.");
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

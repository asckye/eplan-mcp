using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Plans;

internal sealed record PlanBackupAttempt(PlanBackup? Backup, BridgeError? Error, Task Settlement);

/// <summary>
/// Owns one initial backup per call. Caller cancellation and the 75 s wait limit stop waiting only; the native copy
/// cannot be interrupted. A failed attempt stays unusable, even after a late success. No retry or copy deletion.
/// </summary>
internal sealed partial class PlanProjectBackup(IBridgeClient bridge, BackupFolders folders, TimeProvider clock, ILogger<PlanProjectBackup> logger)
{
    public static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(75);
    private const int CopySeconds = 60;

    public async Task<PlanBackupAttempt> RunAsync(string projectPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path;
        try
        {
            path = folders.Create(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSettlementFailed(ex);
            return new(null, BackupFolders.Failure(), Task.CompletedTask);
        }

        // Keep reading the actual reply after the caller stops waiting, so a late copy can be settled safely.
        Task<ProjectBackupData> call = CopyAsync(projectPath, path);
        try
        {
            ProjectBackupData data = await call.WaitAsync(WaitLimit, clock, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(data.Path, path, StringComparison.OrdinalIgnoreCase)
                || data.Pages < 0 || data.Seconds is < 0 or > CopySeconds || !File.Exists(path))
            {
                throw new BridgeCallException(BackupFolders.Failure());
            }

            return new(new(path, data.Pages, data.Seconds, true), null, Task.CompletedTask);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or BridgeCallException)
        {
            BridgeError error = ex is OperationCanceledException ? BridgeErrors.Cancelled()
                : ex is BridgeCallException { Error.Reason: ErrorReason.BackupFailed or ErrorReason.ProjectNotOpen or ErrorReason.Cancelled } known ? known.Error
                : BackupFolders.Failure();
            error.Execution = ExecutionState.NotStarted;
            return new(new(path, null, null, false), error, SettleAfterAsync(call, path));
        }
    }

    private async Task<ProjectBackupData> CopyAsync(string projectPath, string path) =>
        await bridge.InvokeAsync<ProjectBackupArgs, ProjectBackupData>(
            OperationNames.ProjectBackup,
            new() { Project = projectPath, Target = new() { Path = path, MaxSeconds = CopySeconds } },
            Timeout.InfiniteTimeSpan, null, CancellationToken.None).ConfigureAwait(false);

    private async Task SettleAfterAsync(Task<ProjectBackupData> call, string path)
    {
        try
        {
            await call.ConfigureAwait(false);
        }
        catch (BridgeCallException ex) when (ex.Error.Execution == ExecutionState.NotStarted)
        {
            // A terminal operation reply confirms that native work ended; it is safe to rename the folder.
        }
        catch (Exception ex)
        {
            // A lost connection or transport timeout is no proof that EPLAN has stopped using the destination.
            LogSettlementFailed(ex);
            return;
        }

        try
        {
            BackupFolders.Settle(path, failed: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSettlementFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Initial project backup could not be created or marked failed; any copy remains unusable")]
    private partial void LogSettlementFailed(Exception exception);
}

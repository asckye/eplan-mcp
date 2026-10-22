using System.Diagnostics;
using System.Security;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The execution project.create.execute and project.restore.execute share (docs/design/2026-10-21-m6-bridge-snapshot.md,
/// 4.8, 4.9): the arguments and the fingerprint again, the folder still empty, EPLAN's work, the new project's files. There
/// is no transaction: the folder was absent or empty, so on any failure, a cancel included, everything in it is deleted
/// again (and the folder, when this call made it) and the outcome is <c>rolled_back</c>; when not everything can be
/// deleted, an incomplete project may remain, so OUTCOME_UNKNOWN.
/// </summary>
internal static class NewProjectExecution
{
    public static NewProjectData Run(NewProjectArgs args, string extension, Func<NewProjectRequest, NewProjectData> make)
    {
        NewProjectRequest request;
        bool made;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw WriteArguments.Invalid(["'fingerprint' is required: the one the plan returned."]);
            }

            request = NewProjectRequest.Check(args, extension);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The file or the destination folder changed after the plan was made, so nothing was made. Plan again.",
                    retryable: false,
                    new Dictionary<string, string> { ["file"] = request.Source.File });
            }

            // Check found the folder absent or empty in this same main-thread call, right before EPLAN works in it.
            made = !request.Existed;
            Directory.CreateDirectory(request.Destination);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.InvalidArgument, "Invalid arguments: the destination folder cannot be made.", ex));
        }

        var clock = Stopwatch.StartNew();
        try
        {
            NewProjectData data = make(request);
            if (!File.Exists(request.LinkFile) || !Directory.Exists(request.DataFolder))
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN finished without making the new project's files, so no project was made.");
            }

            // The last safe point (M4 design 4.3): a cancel that came meanwhile takes the new project away again.
            OperationContext.Current.ThrowIfCancellationRequested();
            data.Name = request.Name;
            data.Path = request.LinkFile;
            data.Seconds = (int)Math.Round(clock.Elapsed.TotalSeconds);
            return data;
        }
        catch (Exception failure)
        {
            throw Undone(request, made, failure);
        }
    }

    private static Exception Undone(NewProjectRequest request, bool made, Exception failure)
    {
        if (!TryClear(request.Destination, made))
        {
            var unknown = new BridgeErrorException(
                ErrorReason.OutcomeUnknown,
                $"Making project '{request.Name}' did not finish, and the files it made could not all be deleted, so an incomplete project may remain in the destination folder. Ask the engineer to delete that folder.",
                failure);
            unknown.Error.Execution = ExecutionState.Unknown;
            return unknown;
        }

        if (failure is BridgeErrorException known)
        {
            known.Error.Execution = ExecutionState.RolledBack;
            known.Error.Message += " Any files it had made were deleted again.";
        }

        return failure;
    }

    /// <summary>Deletes everything in <paramref name="folder"/>, which was empty before, and with <paramref name="remove"/> the folder.</summary>
    private static bool TryClear(string folder, bool remove)
    {
        try
        {
            var directory = new DirectoryInfo(folder);
            if (directory.Exists)
            {
                foreach (FileSystemInfo entry in directory.GetFileSystemInfos())
                {
                    if (entry is DirectoryInfo subfolder)
                    {
                        ImportedProject.Delete(subfolder);
                    }
                    else
                    {
                        entry.Attributes = FileAttributes.Normal;
                        entry.Delete();
                    }
                }

                if (remove)
                {
                    directory.Delete();
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}

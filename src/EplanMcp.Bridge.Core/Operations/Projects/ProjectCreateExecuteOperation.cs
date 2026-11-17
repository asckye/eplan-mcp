using System.Globalization;
using System.Security;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectCreateExecute"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.8): creates the
/// project from the basic project in the empty folder, as <see cref="NewProjectExecution"/> runs it. The new project is
/// not left open (A34), so it is read with project.snapshot's openReadOnly or opened later.
/// </summary>
public sealed class ProjectCreateExecuteOperation(IProjectFactory factory, IProjectFiles files)
    : Operation<NewProjectArgs, NewProjectData>, IWriteOperation
{
    /// <summary>A basic project (A31).</summary>
    public const string Extension = ".zw9";

    public override string Name => OperationNames.ProjectCreateExecute;

    protected override NewProjectData Execute(NewProjectArgs args)
    {
        string? skipped = UpgradeSkipReason(args.UpgradeFolder);
        try
        {
            return NewProjectExecution.Run(args, Extension, files, request => Create(request, args.UpgradeFolder, skipped));
        }
        finally
        {
            if (skipped is null)
            {
                try
                {
                    ImportedProject.Delete(new DirectoryInfo(args.UpgradeFolder!));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                    OperationContext.Current.Warn("The temporary basic-project upgrade folder could not be deleted.", ex);
                }
            }
        }
    }

    private NewProjectData Create(NewProjectRequest request, string? upgradeFolder, string? skipped)
    {
        try
        {
            (int pages, string? guid) = factory.Create(request.LinkFile, request.Source.FullPath);
            return new NewProjectData { Pages = pages, ProjectGuid = guid };
        }
        catch (BridgeErrorException original) when (original.Error.Reason == ErrorReason.EplanOperationFailed)
        {
            original.Error.Metadata ??= new Dictionary<string, string>();
            if (skipped is not null)
            {
                original.Error.Metadata["upgrade"] = skipped;
                original.Error.Metadata["upgradeMessage"] = "The temporary upgrade folder is unavailable. Check the configured scratch roots.";
                if (skipped == "skipped_path_too_long")
                {
                    original.Error.Metadata["upgradeFolderLength"] = upgradeFolder!.Length.ToString(CultureInfo.InvariantCulture);
                    original.Error.Metadata["upgradeMessage"] = "The temporary upgrade folder exceeds 80 characters. Configure a shorter scratch root.";
                }
                throw;
            }

            bool changed = false;
            bool failed = false;
            try
            {
                changed = factory.UpgradeBasicProject(request.Source.FullPath, upgradeFolder!);
            }
            catch (Exception ex) when (ex is BridgeErrorException or IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
            {
                original.Error.Metadata["upgrade"] = "failed";
                original.Error.Metadata["upgradeMessage"] = "The basic-project copy could not be upgraded. Check EPLAN's messages and scratch folder access.";
                OperationContext.Current.Warn("The basic-project copy could not be upgraded.", ex);
                failed = true;
            }

            if (failed)
            {
                throw;
            }

            if (!changed)
            {
                original.Error.Metadata["upgrade"] = "unchanged";
                throw;
            }

            if (!ImportedProject.TryDelete(request.Target))
            {
                original.Error.Metadata["upgrade"] = "retry_cleanup_failed";
                throw;
            }

            (int pages, string? guid) = factory.Create(request.LinkFile, Path.Combine(upgradeFolder!, Path.GetFileName(request.Source.FullPath)));
            return new NewProjectData { Pages = pages, ProjectGuid = guid, Upgraded = true };
        }
    }

    private static string? UpgradeSkipReason(string? folder)
    {
        if (folder is null)
        {
            return "skipped_unavailable";
        }
        if (folder.Length > 80)
        {
            return "skipped_path_too_long";
        }
        try
        {
            var directory = new DirectoryInfo(folder);
            return ProjectPaths.IsPlainFullPath(folder) && directory.Exists
                && (directory.Attributes & FileAttributes.ReparsePoint) == 0 && !directory.EnumerateFileSystemInfos().Any()
                ? null
                : "skipped_unavailable";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return "skipped_unavailable";
        }
    }
}

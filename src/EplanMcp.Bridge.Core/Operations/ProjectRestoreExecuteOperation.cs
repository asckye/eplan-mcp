using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectRestoreExecute"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.9): restores the
/// backup into the empty folder, as <see cref="NewProjectExecution"/> runs it, right after looking that the folder is
/// still empty in the same main-thread call; then reads the database version without opening the project (A31).
/// </summary>
public sealed class ProjectRestoreExecuteOperation(IProjectFactory factory, IProjectFiles files) : Operation<NewProjectArgs, NewProjectData>, IWriteOperation
{
    /// <summary>A project backup (A31, A35).</summary>
    public const string Extension = ".zw1";

    public override string Name => OperationNames.ProjectRestoreExecute;

    protected override NewProjectData Execute(NewProjectArgs args) => NewProjectExecution.Run(args, Extension, request =>
    {
        factory.Restore(request.Source.FullPath, request.LinkFile);
        return new NewProjectData { DatabaseVersion = File.Exists(request.LinkFile) ? files.Compatibility(request.LinkFile).DatabaseVersion : null };
    });
}

using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectCreateExecute"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.8): creates the
/// project from the basic project in the empty folder, as <see cref="NewProjectExecution"/> runs it. The new project is
/// not left open (A34), so it is read with project.snapshot's openReadOnly or opened later.
/// </summary>
public sealed class ProjectCreateExecuteOperation(IProjectFactory factory, IProjectFiles files) : Operation<NewProjectArgs, NewProjectData>, IWriteOperation
{
    /// <summary>A basic project (A31).</summary>
    public const string Extension = ".zw9";

    public override string Name => OperationNames.ProjectCreateExecute;

    protected override NewProjectData Execute(NewProjectArgs args) => NewProjectExecution.Run(args, Extension, files, request =>
    {
        (int pages, string? guid) = factory.Create(request.LinkFile, request.Source.FullPath);
        return new NewProjectData { Pages = pages, ProjectGuid = guid };
    });
}

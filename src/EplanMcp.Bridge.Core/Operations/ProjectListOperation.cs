using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary><see cref="OperationNames.ProjectList"/>: every open project; paging happens in the Host (tool spec 5).</summary>
public sealed class ProjectListOperation(IProjectCatalog catalog) : Operation<ProjectListArgs, ProjectListData>
{
    public override string Name => OperationNames.ProjectList;

    protected override ProjectListData Execute(ProjectListArgs args) =>
        new() { Projects = [.. catalog.ListOpenProjects()] };
}

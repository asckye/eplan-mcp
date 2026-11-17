using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary><see cref="OperationNames.ProjectUpdatePlan"/>: one <c>update</c> per title block property that would change. Reads only.</summary>
public sealed class ProjectUpdatePlanOperation(IProjectCatalog catalog, IProjectProperties properties, ITextTranslator translator) : Operation<ProjectUpdateArgs, WritePlan>
{
    public override string Name => OperationNames.ProjectUpdatePlan;

    protected override WritePlan Execute(ProjectUpdateArgs args)
    {
        ProjectUpdateRequest request = ProjectUpdateRequest.Check(args, catalog, properties, translator);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Changing.Select(p => new PlannedChange
            {
                Action = "update",
                Kind = "project",
                Name = request.Project.Name,
                Property = p.Key,
                OldValue = p.Before,
                NewValue = p.After,
            })],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

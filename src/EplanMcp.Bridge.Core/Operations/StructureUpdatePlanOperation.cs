using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary><see cref="OperationNames.StructureUpdatePlan"/>: one <c>create</c> or <c>update</c> per identifier that would change. Reads only.</summary>
public sealed class StructureUpdatePlanOperation(IProjectCatalog catalog, IStructureIdentifiers identifiers) : Operation<StructureUpdateArgs, WritePlan>
{
    public override string Name => OperationNames.StructureUpdatePlan;

    protected override WritePlan Execute(StructureUpdateArgs args)
    {
        StructureUpdateRequest request = StructureUpdateRequest.Check(args, catalog, identifiers);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Changes.Select(c => new PlannedChange
            {
                Action = c.Create ? "create" : "update",
                Kind = "structure",
                Name = c.Name,
                Property = c.After is null ? null : "description",
                OldValue = c.Create ? null : c.Before,
                NewValue = c.After,
            })],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

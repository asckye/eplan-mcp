using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary><see cref="OperationNames.StructureDeletePlan"/>: one <c>delete</c> per unused identifier. Reads only.</summary>
public sealed class StructureDeletePlanOperation(IProjectCatalog catalog, IStructureIdentifiers identifiers) : Operation<StructureDeleteArgs, WritePlan>
{
    public override string Name => OperationNames.StructureDeletePlan;

    protected override WritePlan Execute(StructureDeleteArgs args)
    {
        StructureDeleteRequest request = StructureDeleteRequest.Check(args, catalog, identifiers);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Removing.Select(i => new PlannedChange { Action = "delete", Kind = "structure", Name = i.Name, OldValue = i.Description.Length == 0 ? null : i.Description })],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

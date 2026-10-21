using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ImportPxfPlan"/>: checks the file and that no project of the name is in the scratch folder's
/// <c>imported</c> folder, and returns one <c>create</c> change of the new project, whose path the policy finds in scratch
/// (docs/tools/eplan_import_pxf.md, section 5; M4c design Q5). Reads only, and no open project is involved.
/// </summary>
public sealed class ImportPxfPlanOperation : Operation<ImportPxfArgs, WritePlan>
{
    public override string Name => OperationNames.ImportPxfPlan;

    protected override WritePlan Execute(ImportPxfArgs args)
    {
        var request = ImportPxfRequest.Check(args);
        return new WritePlan
        {
            Project = request.Name,
            ProjectPath = request.LinkFile,
            Changes = [request.Change()],
            Fingerprint = request.StateFingerprint(),
            Details = request.Details(),
        };
    }
}

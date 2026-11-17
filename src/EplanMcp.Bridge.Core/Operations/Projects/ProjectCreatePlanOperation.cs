using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectCreatePlan"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.8): checks the basic
/// project file and that the destination folder is absent or empty, and returns one <c>create</c> change whose project path
/// the Host's policy finds in scratch or asks about. Reads only; no open project is involved.
/// </summary>
public sealed class ProjectCreatePlanOperation : Operation<NewProjectArgs, WritePlan>
{
    public override string Name => OperationNames.ProjectCreatePlan;

    protected override WritePlan Execute(NewProjectArgs args)
    {
        var request = NewProjectRequest.Check(args, ProjectCreateExecuteOperation.Extension);
        return new WritePlan
        {
            Project = request.Name,
            ProjectPath = request.LinkFile,
            Changes = [request.Change("from basic project")],
            Fingerprint = request.StateFingerprint(),
            Details = request.Details(),
        };
    }
}

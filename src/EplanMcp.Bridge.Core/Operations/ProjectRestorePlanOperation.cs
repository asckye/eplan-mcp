using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectRestorePlan"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.9): checks the
/// project backup and that the destination folder is absent or empty (EPLAN overwrites a project it restores onto,
/// api-notes A31), and returns one <c>create</c> change. Reads only; no open project is involved.
/// </summary>
public sealed class ProjectRestorePlanOperation : Operation<NewProjectArgs, WritePlan>
{
    public override string Name => OperationNames.ProjectRestorePlan;

    protected override WritePlan Execute(NewProjectArgs args)
    {
        var request = NewProjectRequest.Check(args, ProjectRestoreExecuteOperation.Extension);
        return new WritePlan
        {
            Project = request.Name,
            ProjectPath = request.LinkFile,
            Changes = [request.Change("restored from")],
            Fingerprint = request.StateFingerprint(),
            Details = request.Details(),
        };
    }
}

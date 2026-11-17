using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ConnectionGeneratePlan"/>: finds the pages and returns one <c>update</c> change per page
/// with its connection count now; what EPLAN will add or remove is known only after running, so the new value is null
/// (docs/tools/eplan_connection_generate.md, section 5). Reads only.
/// </summary>
public sealed class ConnectionGeneratePlanOperation(IProjectCatalog catalog, IPageReader reader, IConnectionGenerator generator) : Operation<ConnectionGenerateArgs, WritePlan>
{
    public override string Name => OperationNames.ConnectionGeneratePlan;

    protected override WritePlan Execute(ConnectionGenerateArgs args)
    {
        var request = ConnectionGenerateRequest.Check(args, catalog, reader);
        IReadOnlyList<IReadOnlyList<ConnectionRecord>> connections = generator.ReadConnections(request.Project.Path, request.Pages);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                .. request.Pages.Select((page, i) => new PlannedChange
                {
                    Action = "update",
                    Kind = "page",
                    Name = page,
                    Property = "connections",
                    OldValue = connections[i].Count.ToString(CultureInfo.InvariantCulture),
                }),
            ],
            Fingerprint = request.StateFingerprint(connections, generator.ObjectIds(request.Project.Path, request.Pages)),
        };
    }
}

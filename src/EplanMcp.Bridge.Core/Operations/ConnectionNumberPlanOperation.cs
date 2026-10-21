using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ConnectionNumberPlan"/>: checks the scheme against the project's wire numbering schemes,
/// reads every connection with its wire number and returns one change for the whole numbering, since only the scheme
/// decides the new wire numbers (docs/tools/eplan_connection_number.md, section 5), with the counts in the details and
/// the fingerprint. A project without connections: no change, so nothing is asked. Reads only.
/// </summary>
public sealed class ConnectionNumberPlanOperation(IProjectCatalog catalog, IConnectionNumberer numberer) : Operation<ConnectionNumberArgs, WritePlan>
{
    public override string Name => OperationNames.ConnectionNumberPlan;

    protected override WritePlan Execute(ConnectionNumberArgs args)
    {
        ConnectionNumberRequest request = ConnectionNumberRequest.Check(args, catalog, numberer);
        IReadOnlyList<DesignatedConnection> connections = numberer.ReadConnections(request.Project.Path);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = connections.Count == 0
                ? []
                : [new PlannedChange { Action = "update", Kind = "connections", Name = request.Project.Name, Property = "numbering", OldValue = request.Describe(connections) }],
            Fingerprint = ConnectionNumberRequest.FingerprintOf(connections),
            Details = new Dictionary<string, string>
            {
                ["connections"] = Number(connections.Count),
                ["designatedBefore"] = Number(ConnectionNumberRequest.Designated(connections)),
                ["scheme"] = request.Scheme,
                ["overwrite"] = request.Overwrite,
            },
        };
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

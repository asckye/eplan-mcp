using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads and routes 3D connections (api-notes A141), implemented by the EPLAN adapter; main thread.</summary>
public interface IConnectionRouter
{
    /// <summary>The 3D connections of the installation space <paramref name="space"/> in EPLAN's order; null when the project has no such space.</summary>
    public IReadOnlyList<RoutedConnection>? Read(string projectPath, string space);

    /// <summary>Routes the space's connections that end at one of <paramref name="devices"/>, or all of them when it is empty; inside the transaction.</summary>
    public void Route(string projectPath, string space, IReadOnlyCollection<string> devices, bool network);
}

/// <summary>The checked arguments of eplan_connection_route, shared by its plan and its execution.</summary>
internal sealed class ConnectionRouteRequest
{
    public const int MaxDevices = 200;

    public OpenProject Project { get; private set; } = new();
    public string Space { get; private set; } = string.Empty;
    public bool Network { get; private set; } = true;
    public IReadOnlyCollection<string> Devices { get; private set; } = [];
    public IReadOnlyList<RoutedConnection> Before { get; private set; } = [];

    /// <summary>The connections this call routes: all, or those ending at one of the devices.</summary>
    public IEnumerable<RoutedConnection> Targets(IEnumerable<RoutedConnection> connections) =>
        Devices.Count == 0 ? connections : connections.Where(c => Devices.Contains(c.From) || Devices.Contains(c.To));

    public static ConnectionRouteRequest Check(ConnectionRouteArgs args, IProjectCatalog catalog, IConnectionRouter router)
    {
        var violations = new List<string>();
        string space = (args.Space ?? string.Empty).Trim().TrimStart('+');
        if (space.Length == 0)
        { violations.Add("'space' is required: an installation space designation such as A1, from eplan_layout_get."); }
        string routing = string.IsNullOrWhiteSpace(args.Routing) ? "network" : args.Routing!.Trim();
        if (routing is not "network" and not "direct")
        { violations.Add("'routing' must be 'network' (along ducts and routing segments) or 'direct'."); }
        string[] devices = [.. args.Devices.Select(d => (d ?? string.Empty).Trim()).Distinct(StringComparer.Ordinal)];
        if (devices.Length > MaxDevices || devices.Any(d => d.Length == 0))
        { violations.Add($"'devices' must hold at most {MaxDevices.ToString(CultureInfo.InvariantCulture)} non-empty device tags."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<RoutedConnection> before = router.Read(project.Path, space)
            ?? throw WriteArguments.Invalid([$"'space' names no installation space of this project: {space}. eplan_layout_get lists them."]);
        HashSet<string> ends = [.. before.SelectMany(c => new[] { c.From, c.To })];
        string[] unknown = [.. devices.Where(d => !ends.Contains(d))];
        if (unknown.Length > 0)
        { throw WriteArguments.Invalid([$"'devices' names devices with no 3D connection in {space}: {string.Join(", ", unknown.Take(10))}. Place them with eplan_layout_place first."]); }

        return new ConnectionRouteRequest
        {
            Project = project,
            Space = space,
            Network = routing == "network",
            Devices = new HashSet<string>(devices, StringComparer.Ordinal),
            Before = before,
        };
    }

    /// <summary>The connections' ends and routes and the arguments: routing changes when any of them changes.</summary>
    public string StateFingerprint() => Fingerprint.Of([Project.Path, Space, Network ? "network" : "direct", string.Join("|", Devices.OrderBy(d => d, StringComparer.Ordinal)),
        .. Before.Select(Line)]);

    internal static string Line(RoutedConnection c) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1} > {2}:{3} {4} {5:0.0}", c.From, c.FromPin, c.To, c.ToPin, c.Routed, c.LengthMm);
}

/// <summary><see cref="OperationNames.ConnectionRoutePlan"/>: what eplan_connection_route would route; reads only.</summary>
public sealed class ConnectionRoutePlanOperation(IProjectCatalog catalog, IConnectionRouter router) : Operation<ConnectionRouteArgs, WritePlan>
{
    public override string Name => OperationNames.ConnectionRoutePlan;

    protected override WritePlan Execute(ConnectionRouteArgs args)
    {
        ConnectionRouteRequest request = ConnectionRouteRequest.Check(args, catalog, router);
        RoutedConnection[] targets = [.. request.Targets(request.Before)];
        string count = targets.Length.ToString(CultureInfo.InvariantCulture);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = targets.Length == 0 ? [] :
                [new PlannedChange { Action = "update", Kind = "3D connections", Name = request.Space, Property = "route", NewValue = count + (request.Network ? " along the routing network" : " directly") }],
            Details = new Dictionary<string, string>
            {
                ["connections"] = count,
                ["routedBefore"] = targets.Count(c => c.Routed).ToString(CultureInfo.InvariantCulture),
                ["routing"] = request.Network ? "network" : "direct",
            },
            Fingerprint = request.StateFingerprint(),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.ConnectionRouteExecute"/>: checks again, routes in one transaction and reads the routes back.
/// A connection that stays unrouted is reported, not rolled back: its ends may not be in the 3D layout yet.
/// </summary>
public sealed class ConnectionRouteExecuteOperation(IProjectCatalog catalog, IConnectionRouter router, ITransactionScopeFactory transactions)
    : Operation<ConnectionRouteArgs, ConnectionRouteData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_connection_route".</summary>
    public const string Tool = "eplan_connection_route";

    public override string Name => OperationNames.ConnectionRouteExecute;

    protected override ConnectionRouteData Execute(ConnectionRouteArgs args)
    {
        ConnectionRouteRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one connection.route.plan returned."); }
            request = ConnectionRouteRequest.Check(args, catalog, router);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The 3D connections changed after the plan was made, so nothing was routed. Call eplan_connection_route again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["space"] = request.Space });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<RoutedConnection[]> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            router.Route(request.Project.Path, request.Space, request.Devices, request.Network);
            IReadOnlyList<RoutedConnection> after = router.Read(request.Project.Path, request.Space)
                ?? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The installation space was gone after routing, so nothing was kept.",
                    retryable: false, new Dictionary<string, string> { ["space"] = request.Space });
            return request.Targets(after).ToArray();
        });
        RoutedConnection[] routed = committed.Result;
        return new ConnectionRouteData
        {
            Project = request.Project.Name,
            Space = request.Space,
            Connections = routed.Length,
            Routed = routed.Count(c => c.Routed),
            Items = [.. routed.OrderByDescending(c => c.Routed).ThenByDescending(c => c.LengthMm).ThenBy(c => c.From, StringComparer.Ordinal)],
            Warning = committed.Warning,
        };
    }
}

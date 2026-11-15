using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionRouteTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private readonly Router _router = new();
    private readonly FakeTransactions _transactions = new();

    public ConnectionRouteTests()
    {
        _router.Spaces["A1"] =
        [
            Connection("=GAA+A1-FC1", "2", "=GAA+A1-QA1", "1"),
            Connection("+A1-XD1:1", "#0", "=GAA+A1-FC1", "#0"),
            Connection("=GAA+A1-KF1", "13", "=GAA+A1-QA1", "A1"),
        ];
    }

    [Fact]
    public void Plan_CountsTheConnections_ThenRoutingTakesOneTransactionAndReadsTheRoutesBack()
    {
        var args = new ConnectionRouteArgs { Project = "Line1", Space = "+A1" };
        WritePlan plan = Plan(args);
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("3D connections", "A1", "3 along the routing network"), (change.Kind, change.Name, change.NewValue));
        Assert.Equal(("3", "0", "network"), (plan.Details!["connections"], plan.Details["routedBefore"], plan.Details["routing"]));
        Assert.Empty(_router.Routes);

        args.Fingerprint = plan.Fingerprint;
        ConnectionRouteData data = Execute(args);

        Assert.Equal(("A1", 0, true), Assert.Single(_router.Routes));
        Assert.Equal(("A1", 3, 3), (data.Space, data.Connections, data.Routed));
        Assert.Equal([300d, 200d, 100d], data.Items.Select(i => i.LengthMm));
        Assert.Equal(["Begin AI: eplan_connection_route", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Devices_LimitTheRoutingToTheConnectionsThatEndAtThem()
    {
        var args = new ConnectionRouteArgs { Project = "Line1", Space = "A1", Devices = ["=GAA+A1-QA1"], Routing = "direct" };
        args.Fingerprint = Plan(args).Fingerprint;

        ConnectionRouteData data = Execute(args);

        Assert.Equal(("A1", 1, false), Assert.Single(_router.Routes));
        Assert.Equal(2, data.Connections);
        Assert.All(data.Items, item => Assert.True(item.From == "=GAA+A1-QA1" || item.To == "=GAA+A1-QA1"));
    }

    [Fact]
    public void AConnectionLeftUnrouted_IsReportedAndKept()
    {
        _router.Unroutable.Add("+A1-XD1:1");
        var args = new ConnectionRouteArgs { Project = "Line1", Space = "A1" };
        args.Fingerprint = Plan(args).Fingerprint;

        ConnectionRouteData data = Execute(args);

        Assert.Equal((3, 2), (data.Connections, data.Routed));
        Assert.Equal(("+A1-XD1:1", false, 0d), (data.Items[^1].From, data.Items[^1].Routed, data.Items[^1].LengthMm));
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Fact]
    public void ArgumentProblems_AreInvalidBeforeAnythingIsRouted()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Error(new ConnectionRouteArgs { Project = "Line1", Space = "A1", Routing = "shortest" }).Reason);
        Assert.Contains("names no installation space", Error(new ConnectionRouteArgs { Project = "Line1", Space = "A9" }).Message, StringComparison.Ordinal);
        Assert.Contains("=GAA+A1-XX9", Error(new ConnectionRouteArgs { Project = "Line1", Space = "A1", Devices = ["=GAA+A1-XX9"] }).Message, StringComparison.Ordinal);
        Assert.Contains("at most 200", Error(new ConnectionRouteArgs { Project = "Line1", Space = "A1", Devices = [.. Enumerable.Range(0, 201).Select(i => "D" + i)] }).Message, StringComparison.Ordinal);
        Assert.Empty(_router.Routes);
    }

    [Fact]
    public void Execute_RefusesWithoutTheFingerprint_AndWhenTheConnectionsChanged()
    {
        var args = new ConnectionRouteArgs { Project = "Line1", Space = "A1" };
        BridgeErrorException missing = Assert.Throws<BridgeErrorException>(() => Execute(args));
        args.Fingerprint = Plan(args).Fingerprint;
        _router.Spaces["A1"].Add(Connection("=GAA+A1-FC2", "1", "=GAA+A1-QA1", "2"));
        BridgeErrorException stale = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (missing.Error.Reason, missing.Error.Execution));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (stale.Error.Reason, stale.Error.Execution));
        Assert.Empty(_router.Routes);
    }

    private static RoutedConnection Connection(string from, string fromPin, string to, string toPin) =>
        new() { From = from, FromPin = fromPin, To = to, ToPin = toPin };

    private WritePlan Plan(ConnectionRouteArgs args) => (WritePlan)new ConnectionRoutePlanOperation(new FakeCatalog(_line1), _router).Execute(args)!;

    private ConnectionRouteData Execute(ConnectionRouteArgs args) =>
        (ConnectionRouteData)new ConnectionRouteExecuteOperation(new FakeCatalog(_line1), _router, _transactions).Execute(args)!;

    private BridgeError Error(ConnectionRouteArgs args) => Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

    /// <summary>Routes give each connection 100 mm per position in the space, so the order of the result is checkable.</summary>
    private sealed class Router : IConnectionRouter
    {
        public Dictionary<string, List<RoutedConnection>> Spaces { get; } = new(StringComparer.Ordinal);

        public List<(string Space, int Devices, bool Network)> Routes { get; } = [];

        public HashSet<string> Unroutable { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<RoutedConnection>? Read(string projectPath, string space) =>
            Spaces.TryGetValue(space, out List<RoutedConnection>? connections) ? [.. connections.Select(Copy)] : null;

        public void Route(string projectPath, string space, IReadOnlyCollection<string> devices, bool network)
        {
            Routes.Add((space, devices.Count, network));
            List<RoutedConnection> connections = Spaces[space];
            for (int i = 0; i < connections.Count; i++)
            {
                RoutedConnection c = connections[i];
                if ((devices.Count == 0 || devices.Contains(c.From) || devices.Contains(c.To)) && !Unroutable.Contains(c.From))
                { (c.Routed, c.LengthMm) = (true, 100 * (i + 1)); }
            }
        }

        private static RoutedConnection Copy(RoutedConnection c) =>
            new() { From = c.From, FromPin = c.FromPin, To = c.To, ToPin = c.ToPin, Routed = c.Routed, LengthMm = c.LengthMm };
    }
}

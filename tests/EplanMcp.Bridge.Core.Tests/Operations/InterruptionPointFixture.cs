using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

internal sealed class InterruptionPointFixture : IConnectionCreator, IConnectionGenerator
{
    internal const string PeerPage = "=F+A1&EFS1/2";
    internal const string BusinessName = "=F+A1-LINK";
    internal ConnectionCreateFixture Base { get; } = new();
    internal FakeTransactions Placement { get; } = new();
    internal FakeTransactions Generation { get; } = new();
    internal JArray Peers { get; } = new(Point(PeerPage, 90, [80d, 80d], "Down"));
    internal List<string[]> ScopeReads { get; } = [];
    internal List<string[]> Generates { get; } = [];
    internal string? Failure { get; set; }
    internal int State { get; private set; }
    internal int Undos { get; private set; }
    internal string DrawingRevision { get; set; } = "initial";
    private bool _connected;
    internal JObject Request => Base.Request;
    internal InterruptionPointFixture()
    {
        Base.Symbols.Add(ConnectionCreateFixture.Variant("BPF", "InterruptionPoint", 0, "Up"));
        Base.Scene.SymbolBounds.Add("BPF/0", [-1, -1, 1, 1]);
        ((JArray)Request["items"]!).Add(new JObject
        {
            ["kind"] = "interruption_point",
            ["name"] = BusinessName,
            ["direction"] = "Up",
            ["position"] = new JArray(60, 60),
            ["counterpart_pages"] = new JArray(PeerPage),
        });
        Placement.Snapshot = Generation.Snapshot = () => { int before = State; return () => State = before; };
    }
    internal JObject Plan(string? fingerprint = null) => ConnectionCreatePlanner.Plan(Request.ToString(), Base.Scene, this, fingerprint);
    internal JObject Arguments() => new() { ["request"] = Request.DeepClone(), ["fingerprint"] = Plan()["fingerprint"] };
    internal JObject Execute(JObject? args = null) => Assert.IsType<JObject>(new OperationContext("interruption-test", true, null).Run(() => new ConnectionCreateExecuteOperation(this, this, Generation).Execute(args ?? Arguments())));
    public ConnectionCreatePlanner.Scene Read(string project, string page) => State == 0 ? Base.Scene : new() { Graphical = Geometry() };
    public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => Base.Read(projectPath, args);
    public Action Prepare(string project, string page, JArray symbols) => () =>
    {
        Assert.Contains(symbols, s => (string?)s["interruption"]?["name"] == BusinessName);
        State = 1;
        if (Failure == "placement")
        { throw Failed(); }
    };
    public ITransactionScopeFactory PlacementTransactions(string project) => Placement;
    private List<double[][]> Geometry() => _connected ? [.. BasicGeometry(), new[] { new[] { 60d, 60d }, new[] { 60d, 76d } }] : BasicGeometry();
    private static List<double[][]> BasicGeometry() =>
    [
        [ [8, 8], [24, 8] ],
        [ [24, 8], [24, 24] ],
    ];
    public List<string?[]> Endpoints(string project, string page) => page == PeerPage ? [] : _connected ? [["17/1/0", "17/2/0"], ["17/3/0", "17/9/0"]] : [["17/1/0", "17/2/0"]];
    internal void ConnectPair()
    {
        _connected = true;
        Base.AddPin(3, [60, 76], "Down", [58, 76, 62, 80]);
    }
    public void Generate(string projectPath, IReadOnlyList<string> pages)
    {
        Assert.Equal(1, State);
        Assert.Contains("Commit", Placement.Calls);
        Generates.Add([.. pages]);
        State = 2;
        if (Failure == "generate")
        { throw Failed(); }
        if (Failure == "cancel")
        { OperationContext.Current.Cancel(); }
    }
    public void Undo(string project)
    {
        Undos++;
        State--;
        if (Failure == "undo")
        { throw Failed(); }
    }
    public JObject Interruptions(string project, IReadOnlyList<string> pages)
    {
        ScopeReads.Add([.. pages]);
        Assert.Equal([ConnectionCreateFixture.PageName, PeerPage], pages);
        var points = (JArray)Peers.DeepClone();
        if (State > 0)
        {
            points.Add(Point(ConnectionCreateFixture.PageName, 91, [60d, 60d], "Up"));
            foreach (JToken point in points)
            {
                JToken[] others = points.Where(p => !ReferenceEquals(p, point)).ToArray();
                point["counterparts"] = new JArray(others.Select(p => new JObject { ["page"] = p["page"], ["oid"] = p["oid"], ["name"] = p["name"] }));
                point["crossReference"] = others.Length == 0 ? null : "native:" + (string?)others[0]["page"];
            }
            bool afterCommit = Generation.Calls.Contains("Commit", StringComparer.Ordinal);
            if (State == 2 && (Failure is "pair" or "undo" || Failure == "postcommit" && afterCommit))
            { points.Last!["counterparts"] = new JArray(new JObject { ["page"] = "outside", ["oid"] = 999 }); }
            if (State == 2 && Failure == "empty_reference")
            { points[0]!["crossReference"] = ""; }
        }
        return new()
        {
            ["points"] = points,
            ["pages"] = new JArray(pages.Select(p => new JObject
            {
                ["page"] = p,
                ["drawing"] = BridgeJson.FromObject(Base.Scene.Page),
                ["revision"] = DrawingRevision,
                ["pins"] = p == PeerPage ? _connected ? new JObject { ["17/9/0"] = BridgeJson.FromObject(new SymbolConnectionPoint { Location = [80, 64], Direction = "Up" }) } : new JObject() : BridgeJson.FromObject(Base.Scene.Pins),
                ["graphical"] = p == PeerPage && _connected ? JArray.FromObject(new double[][][] { [[80, 64], [80, 80]] }) : new JArray(),
                ["logical"] = p == PeerPage && _connected ? BridgeJson.FromObject(new[] { new ConnectionRecord { EndDevice = "17/9/0" } }) : new JArray(),
            })),
        };
    }
    internal static JObject Point(string page, long oid, double[] position, string direction) => new()
    {
        ["page"] = page,
        ["name"] = BusinessName,
        ["oid"] = oid,
        ["position"] = new JArray(position),
        ["pin"] = new JArray(position),
        ["direction"] = direction,
        ["crossReference"] = null,
        ["counterparts"] = new JArray(),
    };
    public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
    public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
    private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed, "Injected interruption failure.");
}

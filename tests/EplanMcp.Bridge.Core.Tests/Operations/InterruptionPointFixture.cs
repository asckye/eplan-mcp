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
    internal List<string?[]>? GeneratedEndpoints { get; set; }
    internal int State { get; private set; }
    internal int Undos { get; private set; }
    internal string DrawingRevision { get; set; } = "initial";
    private bool _connected;
    internal long? PlacedSortCode { get; private set; }
    internal bool Isolated { get; set; }
    internal bool Dangling { get; set; }
    internal bool OffsetNode { get; set; }
    internal JObject Request => Base.Request;
    internal InterruptionPointFixture()
    {
        Base.Symbols.AddRange(BpVariants());
        foreach (SymbolSearchItem symbol in Base.Symbols.Where(s => s.Name == "BP"))
        { Base.Scene.SymbolBounds.Add(FormattableString.Invariant($"BP/{symbol.Variant}"), [-1, -1, 1, 1]); }
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
    // A83, exact enumerated BP geometry; variants 4..7 repeat 0..3.
    internal static IEnumerable<SymbolSearchItem> BpVariants()
    {
        string[][] directions = [["Right", "Left"], ["Up", "Down"], ["Left", "Right"], ["Down", "Up"]];
        for (int variant = 0; variant < 8; variant++)
        {
            SymbolSearchItem symbol = ConnectionCreateFixture.Variant("BP", "InterruptionPoint", variant, directions[variant % 4]);
            symbol.Kind = "interruption_point";
            symbol.ConnectionPoints[0].Location = variant % 2 == 1 ? [0, -1] : [0, 0];
            symbol.ConnectionPoints[1].Location = variant % 4 == 1 ? [0, -1] : [0, 0];
            yield return symbol;
        }
    }
    internal JObject Plan(string? fingerprint = null) => ConnectionCreatePlanner.Plan(Request.ToString(), Base.Scene, this, fingerprint);
    internal JObject Arguments() => new() { ["request"] = Request.DeepClone(), ["fingerprint"] = Plan()["fingerprint"] };
    internal JObject Execute(JObject? args = null) => Assert.IsType<JObject>(new OperationContext("interruption-test", true, null).Run(() => new ConnectionCreateExecuteOperation(this, this, Generation).Execute(args ?? Arguments())));
    public ConnectionCreatePlanner.Scene Read(string project, string page) => State == 0 ? Base.Scene : new() { Page = Base.Scene.Page, Layout = Base.Scene.Layout, Pins = Base.Scene.Pins, Graphical = Geometry() };
    public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => Base.Read(projectPath, args);
    public LayoutGuard.Model Measure(string project, string page) => LayoutGuard.From(Read(project, page));
    public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null) => () =>
    {
        Assert.Contains(symbols, s => (string?)s["interruption"]?["name"] == BusinessName);
        State = 1;
        PlacedSortCode = (long?)Assert.Single(symbols, s => s["interruption"] is not null)["interruption"]!["sort_code"];
        if (Failure == "cancel")
        { OperationContext.Current.Cancel(); }
        if (Failure == "placement")
        { throw Failed(); }
    };
    public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false)
    { Assert.True(undoCommitted); if (Failure == "undo") { Placement.FailAt = "Rollback"; } return Placement; }
    private List<double[][]> Geometry() => Failure == "line" || State == 2 && Failure == "generated_line" ? [] : Isolated ? [] : Dangling ? [[[60, 60], [60, 76]]] : _connected ? [.. BasicGeometry(), new[] { new[] { 60d, 60d }, new[] { 60d, 76d } }] : BasicGeometry();
    private static List<double[][]> BasicGeometry() =>
    [
        [ [8, 8], [24, 8] ],
        [ [24, 8], [24, 24] ],
    ];
    public List<string?[]> Endpoints(string project, string page) => page == PeerPage ? [] : GeneratedEndpoints ?? (_connected ? [["17/1/0", "17/2/0"], ["17/3/0", "17/9/0"]] : [["17/1/0", "17/2/0"]]);
    internal void ConnectPair()
    {
        _connected = true;
        Base.AddPin(3, [60, 76], "Down", [58, 76, 62, 80]);
    }
    public void Generate(string projectPath, IReadOnlyList<string> pages)
    {
        Assert.Equal(1, State);
        Assert.Contains("Commit", Placement.Calls);
        Assert.DoesNotContain("CloseUndo", Placement.Calls);
        Placement.Calls.Add("Generate");
        Generates.Add([.. pages]);
        State = 2;
        if (Failure == "generate")
        { throw Failed(); }

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
            points.Last!["sortCode"] = Failure == "sort_code" ? "999" : PlacedSortCode?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Request["items"]!.Last!["symbol_variant"] is { } variant)
            {
                points.Last!["direction"] = "Left"; // BP/0's last pin; the requested Right pin is index zero.
                points.Last!["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = "BP", ["variant"] = variant.DeepClone() };
                points.Last!["pins"] = new JArray(new JObject { ["position"] = new JArray(60d, 60d), ["direction"] = "Right" });
            }
            foreach (JToken point in points)
            {
                JToken[] others = points.Where(p => !ReferenceEquals(p, point)).ToArray();
                point["counterparts"] = new JArray(others.Select(p => new JObject { ["page"] = p["page"], ["oid"] = p["oid"], ["name"] = p["name"] }));
                point["crossReference"] = others.Length == 0 ? null : "native:" + (string?)others[0]["page"];
            }
            bool afterCommit = Placement.Calls.Contains("Commit", StringComparer.Ordinal);
            if (State == 2 && (Failure is "pair" or "undo" || Failure == "postcommit" && afterCommit))
            { points.Last!["counterparts"] = new JArray(new JObject { ["page"] = "outside", ["oid"] = 999 }); }
            if (State == 2 && Failure is "position" or "pin" or "direction" or "name")
            { points.Last![Failure] = Failure is "position" or "pin" ? new JArray(64d, 60d) : "wrong"; }
            if (State == 2 && Failure == "empty_reference")
            { points[0]!["crossReference"] = ""; }
        }
        JObject scope = new()
        {
            ["points"] = points,
            ["pages"] = new JArray(pages.Select(p => new JObject
            {
                ["page"] = p,
                ["drawing"] = BridgeJson.FromObject(Base.Scene.Page),
                ["revision"] = DrawingRevision,
                ["pins"] = p == PeerPage ? _connected && !Dangling ? new JObject { ["17/9/0"] = BridgeJson.FromObject(new SymbolConnectionPoint { Location = [80, 64], Direction = "Up" }) } : new JObject() : BridgeJson.FromObject(Base.Scene.Pins),
                // OffsetNode: the peer route passes a node whose two pins are 2 mm apart (A64 variant 0).
                ["graphical"] = p == PeerPage && _connected && !Dangling ? JArray.FromObject(OffsetNode ? new double[][][] { [[80, 64], [80, 70]], [[80, 72], [80, 80]] } : new double[][][] { [[80, 64], [80, 80]] }) : new JArray(),
                ["nodes"] = p == PeerPage && OffsetNode ? JArray.FromObject(new double[][][] { [[80, 70], [80, 72]] }) : new JArray(),
                ["logical"] = p == PeerPage && _connected && !Dangling ? BridgeJson.FromObject(new[] { new ConnectionRecord { EndDevice = "17/9/0" } }) : new JArray(),
            })),
        };
        if (State == 2 && Failure == "peer_line")
        { scope["pages"]![1]!["graphical"] = JArray.FromObject(new double[][][] { [[80, 64], [84, 80]] }); }
        if (State == 2 && Failure == "peer_pin")
        { scope["pages"]![1]!["pins"]!["17/999/0"] = new JObject { ["location"] = new JArray(84, 80), ["direction"] = "Up" }; }
        return scope;
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
        ["sortCode"] = null,
        ["counterparts"] = new JArray(),
    };
    public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
    public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
    private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed, "Injected interruption failure.");
}

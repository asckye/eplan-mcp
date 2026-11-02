using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionPointRoutingTests
{
    [Fact]
    public void PagePoint_NotListedByAnyConnection_ReportsMissingMembership()
    {
        var scene = Scene();
        ConnectionPointRouting.Initialize(scene);
        Assert.Empty(scene.PointRoutes);
        Assert.Equal("no connection lists it", scene.PointRouteGaps["59/69919"]);
        ConnectionPointRouting.Record(scene, ["59/69920"], "17/1/0", "17/2/0");
        Assert.Equal("no connection lists it", scene.PointRouteGaps["59/69919"]);
    }

    [Theory]
    [InlineData(null, "17/2/0", "pins unresolved <missing>/17/2/0")]
    [InlineData("17/1/0", null, "pins unresolved 17/1/0/<missing>")]
    [InlineData(null, null, "pins unresolved <missing>/<missing>")]
    [InlineData("17/56/0", "17/2/0", "pins unresolved 17/56/0/17/2/0")]
    [InlineData("17/1/0", "17/80/0", "pins unresolved 17/1/0/17/80/0")]
    public void ListedPoint_MissingEndpointOrUnresolvedPinKey_ReportsBothKeys(string? start, string? end, string reason)
    {
        var scene = Scene();
        ConnectionPointRouting.Initialize(scene);
        ConnectionPointRouting.Record(scene, ["59/69919"], start, end);
        Assert.Empty(scene.PointRoutes);
        Assert.Equal(reason, scene.PointRouteGaps["59/69919"]);
    }

    [Theory]
    [InlineData(0, "no drawn path")]
    [InlineData(2, "drawn path not unique (2 paths)")]
    public void ListedPoint_ResolvedPinsWithoutUniquePath_ReportsSearchCount(int pathCount, string reason)
    {
        var scene = Scene();
        if (pathCount == 2)
        { AddPaths(scene); }
        ConnectionPointRouting.Initialize(scene);
        Assert.Null(CableDefinitionPlanner.DrawnPath(scene, [16, 16], [32, 32], out int actualCount));
        Assert.Equal(pathCount, actualCount);
        ConnectionPointRouting.Record(scene, ["59/69919"], "17/1/0", "17/2/0");
        Assert.Empty(scene.PointRoutes);
        Assert.Equal(reason, scene.PointRouteGaps["59/69919"]);
    }

    [Fact]
    public void UniquePath_RecordsOwningSegmentsAndClearsGap_DespiteOtherUnreadableConnections()
    {
        var scene = Scene();
        AddPaths(scene);
        scene.Graphical.RemoveRange(2, 2);
        scene.Graphical.Add(scene.Graphical[0]);
        ConnectionPointRouting.Initialize(scene);
        ConnectionPointRouting.Record(scene, ["59/69919"], null, null);
        Assert.StartsWith("pins unresolved", scene.PointRouteGaps["59/69919"], StringComparison.Ordinal);
        double[][]? path = CableDefinitionPlanner.DrawnPath(scene, [16, 16], [32, 32], out int count);
        Assert.Equal(1, count);
        Assert.Equal(CableDefinitionPlanner.DrawnPath(scene, [16, 16], [32, 32]), path);
        ConnectionPointRouting.Record(scene, ["59/69919"], "17/1/0", "17/2/0");
        Assert.Equal(2, scene.PointRoutes["59/69919"].Count);
        Assert.Equal(new double[][] { [16, 16], [16, 32] }, scene.PointRoutes["59/69919"][0]);
        Assert.False(scene.PointRouteGaps.ContainsKey("59/69919"));
        ConnectionPointRouting.Record(scene, ["59/69919"], null, null);
        Assert.False(scene.PointRouteGaps.ContainsKey("59/69919"));
    }

    private static ConnectionCreatePlanner.Scene Scene() => new()
    {
        Page = new() { Placements = [new() { Kind = "ConnectionDefinitionPoint", Locator = new() { Type = 59, Oid = 69919 } }] },
        Pins = new()
        {
            ["17/1/0"] = new() { Location = [16, 16] },
            ["17/2/0"] = new() { Location = [32, 32] }
        }
    };

    private static void AddPaths(ConnectionCreatePlanner.Scene scene)
    {
        scene.Page.Placements.Add(new() { Kind = "SymbolReference", Locator = new() { Type = 13, Oid = 3 } });
        scene.Page.Placements.Add(new() { Kind = "SymbolReference", Locator = new() { Type = 13, Oid = 4 } });
        scene.Graphical = [[[16, 16], [16, 32]], [[16, 32], [32, 32]], [[16, 16], [32, 16]], [[32, 16], [32, 32]]];
        scene.Pins["13/3/0"] = new() { Location = [16, 32] };
        scene.Pins["13/3/1"] = new() { Location = [16, 32] };
        scene.Pins["13/4/0"] = new() { Location = [32, 16] };
        scene.Pins["13/4/1"] = new() { Location = [32, 16] };
    }

    [Fact]
    public void LiveConnection72612_ThroughTNode_RecordsCdpOwningRouteWithoutGapOrR4Warning()
    {
        var scene = TNodeFixture.Scene();
        // A synthetic CDP on the route whose device endpoints and T-node geometry were read live.
        scene.Page.Placements.Add(new()
        {
            Kind = "ConnectionDefinitionPoint",
            Locator = new() { Type = 59, Oid = 1 },
            Location = [120, 192],
            Bounds = [118.75, 190.75, 121.25, 193.25]
        });
        ConnectionPointRouting.Initialize(scene);
        // Live connection 72612 joins R103A (69842) to R103B (69843).
        ConnectionPointRouting.Record(scene, ["59/1"], "17/69842/0", "17/69843/0");
        Assert.Empty(scene.PointRouteGaps);
        Assert.Equal(2, scene.PointRoutes["59/1"].Count);
        Assert.Equal(new double[][] { [120, 220], [120, 160] }, scene.PointRoutes["59/1"][0]);
        Assert.Equal(new double[][] { [120, 160], [120, 100] }, scene.PointRoutes["59/1"][1]);
        LayoutGuard.Model model = LayoutGuard.From(scene);
        foreach (LayoutGuard.Segment line in model.Lines)
        { line.Changed = true; }
        Assert.DoesNotContain(LayoutGuard.Check(model, [], false), v => v.Rule == "R4");
    }
}

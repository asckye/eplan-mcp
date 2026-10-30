using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionCreateGeometryTests
{
    [Theory]
    [InlineData("Up", 0)]
    [InlineData("Down", 0)]
    [InlineData("Left", 0)]
    [InlineData("Right", 0)]
    [InlineData("Up", 2)]
    [InlineData("Down", 2)]
    [InlineData("Left", 2)]
    [InlineData("Right", 2)]
    public void Autoconnect_EndpointOnOrInsideItsOwnPlacementOutline_Accepts(string direction, double inset)
    {
        var fixture = Empty();
        int axis = direction is "Left" or "Right" ? 0 : 1;
        int sign = direction is "Up" or "Right" ? 1 : -1;
        double[] pin = [40, 40];
        pin[axis] += sign * 16;
        double[] bounds = [pin[0] - 2, pin[1] - 2, pin[0] + 2, pin[1] + 2];
        bounds[axis] = pin[axis] - (sign > 0 ? inset : 4);
        bounds[axis + 2] = pin[axis] + (sign > 0 ? 4 : inset);
        fixture.AddPin(9, pin, Opposite(direction), bounds);
        fixture.Symbols[2].ConnectionPoints[0].Direction = direction;
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [40, 40], direction));
        JObject plan = fixture.Plan();
        Assert.Single((JArray)plan["graphicalAutoconnect"]!);
        Assert.Equal(["17/9/0"], plan["logicalAfterGenerate"]![0]!["pins"]!.Values<string>());
    }

    [Fact]
    public void Wire_BothEndpointPinsInsideTheirOwnOutlines_Accepts()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.Placements[0].Bounds = [4, 6, 10, 10];
        fixture.Scene.Page.Placements[1].Bounds = [22, 22, 26, 28];
        Assert.Equal(2, ((JArray)fixture.Plan()["graphicalAutoconnect"]!).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wire_OwnEndpointHasLineOrPointOutline_Accepts(bool point)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.Placements[0].Bounds = point ? [8, 8, 8, 8] : [8, 4, 8, 12];
        Assert.Equal(2, ((JArray)fixture.Plan()["graphicalAutoconnect"]!).Count);
    }

    [Theory]
    [InlineData(17, 3, false)]
    [InlineData(18, 1, false)]
    [InlineData(17, 3, true)]
    public void Wire_OtherPlacementCrossesTerminalApproach_Rejects(int type, long oid, bool degenerate)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.Placements.Add(new() { Locator = new() { Type = type, Oid = oid }, Bounds = degenerate ? [12, 4, 12, 12] : [10, 4, 14, 12] });
        RejectsOutline(fixture);
    }

    [Fact]
    public void Wire_PassesThroughTargetOutlineBeforeReachingOutsidePin_Rejects()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.Placements[1].Bounds = [22, 12, 26, 20];
        RejectsOutline(fixture);
    }

    [Fact]
    public void Wire_IntermediateSegmentCrossesItsTargetPlacementAwayFromEndpoint_Rejects()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Pins["17/2/0"].Direction = "Right";
        fixture.Scene.Page.Placements[1].Bounds = [18, 6, 26, 26];
        fixture.Symbols.Add(ConnectionCreateFixture.Variant("CO", "Routing", 1, "Down", "Left"));
        fixture.Scene.SymbolBounds["CO/1"] = [-1, -1, 1, 1];
        fixture.Request["items"]![0]!["points"] = JArray.FromObject(new double[][] { [8, 8], [40, 8], [40, 24], [24, 24] });
        RejectsOutline(fixture);
    }

    [Theory]
    [InlineData("Up", 0, 0, 3)]
    [InlineData("Left", 1, -2, 0)]
    [InlineData("Down", 2, 0, -4)]
    [InlineData("Right", 3, 2, 0)]
    public void PotentialPoint_S40OffsetAndMirroredVariant_UsesLowestVariantAndAlignsElectricalPin(string direction, int variant, double x, double y)
    {
        var fixture = Empty();
        fixture.Symbols.Clear();
        foreach (int number in new[] { variant + 4, variant })
        {
            SymbolSearchItem symbol = ConnectionCreateFixture.Variant("PCP", "PotentialTerminal", number, direction);
            symbol.ConnectionPoints[0].Location = [x, y];
            fixture.Symbols.Add(symbol);
            fixture.Scene.SymbolBounds["PCP/" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)] = [-2, -2, 2, 2];
        }
        double[] target = [40 + (direction == "Right" ? 16 : direction == "Left" ? -16 : 0), 40 + (direction == "Up" ? 16 : direction == "Down" ? -16 : 0)];
        fixture.AddPin(9, target, Opposite(direction), [target[0] - 2, target[1] - 2, target[0] + 2, target[1] + 2]);
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [40, 40], direction));
        JObject plan = fixture.Plan();
        Assert.Equal(variant, (int)plan["symbols"]![0]!["symbol"]!["variant"]!);
        Assert.Equal([40 - x, 40 - y], plan["symbols"]![0]!["position"]!.Values<double>());
        Assert.Equal([38 - x, 38 - y, 42 - x, 42 - y], plan["symbols"]![0]!["bounds"]!.Values<double>());
        Assert.Equal([40d, 40d], plan["graphicalAutoconnect"]![0]![0]!.Values<double>());
        Assert.True((bool)plan["logicalAfterGenerate"]![0]!["potentialStartIsNull"]!);
        fixture.Symbols.Reverse();
        Assert.Equal((string?)plan["fingerprint"], (string?)fixture.Plan((string)plan["fingerprint"]!)["fingerprint"]);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("nonfinite")]
    [InlineData("missing_axis")]
    [InlineData("distant")]
    [InlineData("multiple_pins")]
    [InlineData("definition")]
    public void PotentialPoint_UnverifiedOrAmbiguousGeometry_FailsClosed(string invalid)
    {
        var fixture = Empty();
        SymbolSearchItem symbol = fixture.Symbols[2];
        switch (invalid)
        {
            case "duplicate":
                fixture.Symbols.Add(symbol);
                break;
            case "nonfinite":
                symbol.ConnectionPoints[0].Location = [double.NaN, 0];
                break;
            case "missing_axis":
                symbol.ConnectionPoints[0].Location = [0];
                break;
            case "distant":
                symbol.ConnectionPoints[0].Location = [0, 100];
                break;
            case "multiple_pins":
                symbol.ConnectionPoints.Add(new() { Location = [0, 0], Direction = "Up" });
                break;
            case "definition":
                symbol.Type = "PotentialDefinition";
                break;
        }
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [40, 40], "Up"));
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void PotentialPoint_OffsetMovesInsertionOutsidePage_Rejects()
    {
        var fixture = Empty();
        fixture.Symbols[2].ConnectionPoints[0].Location = [0, 3];
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [40, 0], "Up"));
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error.Reason);
    }

    [Theory]
    [InlineData("corner", "Up,Left")]
    [InlineData("corner", "Up,Right")]
    [InlineData("corner", "Down,Left")]
    [InlineData("corner", "Down,Right")]
    [InlineData("t_node", "Up,Left,Right")]
    [InlineData("t_node", "Down,Left,Right")]
    [InlineData("t_node", "Up,Down,Left")]
    [InlineData("t_node", "Up,Down,Right")]
    [InlineData("potential_point", "Up")]
    [InlineData("potential_point", "Down")]
    [InlineData("potential_point", "Left")]
    [InlineData("potential_point", "Right")]
    public void LargeLayout_AllDirections_HasNoFacingFixturePairAndPredictsEveryEndpoint(string kind, string directions)
    {
        var fixture = Empty();
        fixture.Scene.Page.PageSize = [240, 240];
        fixture.Symbols.Clear();
        var layout = new ConnectionCreateLayout(4, directions.Split(','));
        var nodes = new List<(string Kind, double[] Position, string[] Directions)> { (kind, layout.Position, directions.Split(',')) };
        if (layout.Corner is { } corner)
        { nodes.Add(("corner", corner.Position, corner.Directions)); }
        int number = 0;
        foreach (var node in nodes)
        {
            string name = node.Kind switch { "corner" => "CO", "potential_point" => "PCP", _ => directions switch { "Up,Left,Right" => "TLRO", "Down,Left,Right" => "TLRU", "Up,Down,Left" => "TOUL", _ => "TOUR" } };
            string type = name switch { "CO" => "Routing", "PCP" => "PotentialTerminal", "TLRO" => "TNodeUp", "TLRU" => "TNodeDown", "TOUL" => "TNodeLeft", _ => "TNodeRight" };
            fixture.Symbols.Add(ConnectionCreateFixture.Variant(name, type, number, node.Directions));
            fixture.Scene.SymbolBounds[FormattableString.Invariant($"{name}/{number++}")] = [0, 0, 0, 0];
        }
        long oid = 1;
        foreach (var target in layout.Targets)
        {
            fixture.AddPin(oid++, target.Pin, target.Facing, [target.Pin[0] - 2, target.Pin[1] - 2, target.Pin[0] + 2, target.Pin[1] + 2]);
            // The helper chooses a two-pin function; model the second pin facing away from the node too.
            double[] other = [target.Pin[0] + (target.Facing == "Left" ? 8 : target.Facing == "Right" ? -8 : 0), target.Pin[1] + (target.Facing == "Down" ? 8 : target.Facing == "Up" ? -8 : 0)];
            fixture.Scene.Pins.Add(FormattableString.Invariant($"17/{oid - 1}/1"), new() { Location = other, Direction = Opposite(target.Facing) });
        }
        Assert.DoesNotContain(fixture.Scene.Pins.Values, a => fixture.Scene.Pins.Values.Any(b => a.Direction == Opposite(b.Direction)
            && (a.Direction == "Up" && a.Location[0] == b.Location[0] && a.Location[1] < b.Location[1]
                || a.Direction == "Right" && a.Location[1] == b.Location[1] && a.Location[0] < b.Location[0])));
        fixture.Request["items"] = new JArray(nodes.Select(n => ConnectionCreateFixture.Node(n.Kind, n.Position, n.Directions)));
        JObject plan = fixture.Plan();
        Assert.Equal(layout.Targets.Count + (layout.Corner is null ? 0 : 1), ((JArray)plan["graphicalAutoconnect"]!).Count);
        Assert.Equal(layout.Targets.Count, plan["logicalAfterGenerate"]![0]!["pins"]!.Count());
    }

    private static ConnectionCreateFixture Empty()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Pins.Clear();
        fixture.Scene.Page.Placements.Clear();
        return fixture;
    }

    private static void RejectsOutline(ConnectionCreateFixture fixture)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Matches(@"/items: The path [0-9.,]+ -> [0-9.,]+ crosses the outline of \w* ?\d+/\d+ \[", error.Metadata!["violations"]);
    }

    private static string Opposite(string direction) => direction switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", _ => "Left" };
}

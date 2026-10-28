using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionCreatePlannerTests
{
    [Fact]
    public void Path_ExplicitOrthogonalBend_ResolvesSpecialCornerAndPinEndpoints()
    {
        var fixture = new ConnectionCreateFixture();
        JObject result = fixture.Plan();
        Assert.Equal("not_started", (string?)result["execution"]);
        Assert.Equal("SPECIAL", fixture.Search!.Library);
        Assert.Equal("scratch-project", fixture.ProjectPath);
        Assert.Equal("CO", (string?)result["symbols"]![0]!["symbol"]!["name"]);
        Assert.Equal(2, ((JArray)result["graphicalAutoconnect"]!).Count);
        Assert.Equal(["17/1/0", "17/2/0"], result["logicalAfterGenerate"]![0]!["pins"]!.Values<string>());
        Assert.Equal(0, (int)result["logicalDuringPlacement"]!);
        Assert.Equal(2, (int)result["undoSteps"]!);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[8,8]]")]
    [InlineData("[[8,8],[24,24]]")]
    [InlineData("[[8,8],[8,8],[24,24]]")]
    [InlineData("[[8,8],[20,8],[16,8],[16,24],[24,24]]")]
    public void Path_InvalidLengthDiagonalDuplicatesOrReversal_Rejects(string points)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request["items"]![0]!["points"] = JArray.Parse(points);
        Invalid(fixture, "/items");
    }

    [Fact]
    public void Path_TooManyPoints_RejectsBeforeResolvingSymbols()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request["items"]![0]!["points"] = new JArray(Enumerable.Range(0, 33).Select(i => new JArray(i * 4, 8)));
        Invalid(fixture, "/items/0/points");
        Assert.Equal(0, fixture.Reads);
    }

    [Theory]
    [InlineData("Left")]
    [InlineData("Up")]
    [InlineData("Down")]
    public void Path_IncompatibleStartDirection_Rejects(string direction)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Pins["17/1/0"].Direction = direction;
        Invalid(fixture, "/items/0");
    }

    [Fact]
    public void Path_IncompatibleEndDirection_Rejects()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Pins["17/2/0"].Direction = "Up";
        Invalid(fixture, "/items/0");
    }

    [Fact]
    public void Path_ThirtyTwoCollinearPoints_AcceptsMaximumLength()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.PageSize = [200, 100];
        JObject from = (JObject)fixture.Request["items"]![0]!["from"]!;
        JObject to = fixture.AddPin(3, [132, 8], "Left", [132, 6, 136, 10]);
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Wire(from, to, Enumerable.Range(0, 32).Select(i => new double[] { 8 + i * 4, 8 }).ToArray()));
        Assert.Equal(31, ((JArray)fixture.Plan()["graphicalAutoconnect"]!).Count);
    }

    [Fact]
    public void Path_AmbiguousCrossingWithoutExplicitTNode_Rejects()
    {
        var fixture = new ConnectionCreateFixture();
        JObject from = fixture.AddPin(3, [16, 0], "Up", [14, 0, 18, 0]);
        JObject to = fixture.AddPin(4, [16, 16], "Down", [14, 16, 18, 20]);
        ((JArray)fixture.Request["items"]!).Add(ConnectionCreateFixture.Wire(from, to, [16, 0], [16, 16]));
        Invalid(fixture, "/items");
    }

    [Fact]
    public void Path_CollinearOverlapOrUndeclaredPin_Rejects()
    {
        var fixture = new ConnectionCreateFixture();
        ((JArray)fixture.Request["items"]!).Add(fixture.Request["items"]![0]!.DeepClone());
        Invalid(fixture, "/items");
        fixture = new();
        fixture.AddPin(3, [16, 8], "Left", [16, 8, 16, 8]);
        Invalid(fixture, "/items");
    }

    [Fact]
    public void Snap_RoundsAwayFromZeroAndReportsRequestedAndActual()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request["items"]![0]!["points"]![1] = new JArray(22, 9);
        JObject result = fixture.Plan();
        Assert.Equal([22d, 9d], result["coordinates"]![1]!["requested"]!.Values<double>());
        Assert.Equal([24d, 8d], result["coordinates"]![1]!["actual"]!.Values<double>());
        fixture.Request["snap_to_grid"] = false;
        Invalid(fixture, "/items/0/points/1");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    [InlineData("-4")]
    [InlineData("10001")]
    [InlineData("\"8\"")]
    public void Coordinates_NonfiniteOutOfRangeOrString_RejectsWithPointer(string value)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request["items"]![0]!["points"]![0]![0] = JToken.Parse(value);
        Invalid(fixture, "/items/0/points/0");
    }

    [Theory]
    [InlineData("corner", "[\"Left\",\"Right\"]")]
    [InlineData("corner", "[\"Up\",\"Up\"]")]
    [InlineData("t_node", "[\"Left\",\"Right\",\"Up\",\"Down\"]")]
    [InlineData("potential_point", "[\"Undefined\"]")]
    [InlineData("interruption_point", "[\"Up\"]")]
    public void Node_UnsupportedKindOrDirections_Rejects(string kind, string directions)
    {
        var fixture = new ConnectionCreateFixture();
        JObject node = ConnectionCreateFixture.Node(kind, [40, 40]);
        node["directions"] = JArray.Parse(directions);
        fixture.Request["items"] = new JArray(node);
        Invalid(fixture, "/items/0");
    }

    [Theory]
    [InlineData("/unexpected~1member~0", "{\"unexpected/member~\":1}")]
    [InlineData("/items/0/extra", "{\"items\":[{\"kind\":\"potential_point\",\"position\":[40,40],\"directions\":[\"Up\"],\"extra\":1}]}")]
    [InlineData("/dry_run", "{\"dry_run\":null}")]
    [InlineData("/response_format", "{\"response_format\":\"xml\"}")]
    [InlineData("/items/0/kind", "{\"items\":[1]}")]
    public void Input_StrictMembersTypesAndPointerEscaping_Rejects(string at, string patch)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request.Merge(JObject.Parse(patch), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge });
        Invalid(fixture, at);
    }

    [Fact]
    public void Input_DuplicateMembersAndTrailingJson_Rejects()
    {
        var fixture = new ConnectionCreateFixture();
        string json = fixture.Request.ToString();
        foreach (string invalid in new[] { json.Replace("\"dry_run\": true", "\"dry_run\": true,\"dry_run\": true", StringComparison.Ordinal), json + "{}" })
        {
            BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => ConnectionCreatePlanner.Plan(invalid, fixture.Scene, fixture));
            Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Input_ItemCountOutsideLimit_Rejects(int count)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Request["items"] = new JArray(Enumerable.Range(0, count).Select(_ => fixture.Request["items"]![0]!.DeepClone()));
        Invalid(fixture, "/items");
    }

    [Fact]
    public void Execution_DefaultOrFalse_IsCapabilityMissingBeforeAnyReaderCall()
    {
        var fixture = new ConnectionCreateFixture();
        foreach (bool omit in new[] { false, true })
        {
            if (omit)
            { fixture.Request.Remove("dry_run"); }
            else
            { fixture.Request["dry_run"] = false; }
            BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => fixture.Plan());
            Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (ex.Error.Reason, ex.Error.Execution));
            Assert.Equal("connection.create.execute", ex.Error.Metadata!["missing"]);
        }
        Assert.Equal(0, fixture.Reads);
    }

    [Theory]
    [InlineData("CO", "Routing", "corner", "Up", "Left")]
    [InlineData("CO", "Routing", "corner", "Up", "Right")]
    [InlineData("CO", "Routing", "corner", "Down", "Left")]
    [InlineData("CO", "Routing", "corner", "Down", "Right")]
    [InlineData("TLRO", "TNodeUp", "t_node", "Up", "Left", "Right")]
    [InlineData("TLRU", "TNodeDown", "t_node", "Down", "Left", "Right")]
    [InlineData("TOUL", "TNodeLeft", "t_node", "Up", "Down", "Left")]
    [InlineData("TOUR", "TNodeRight", "t_node", "Up", "Down", "Right")]
    [InlineData("PCP", "PotentialTerminal", "potential_point", "Up")]
    [InlineData("PCP", "PotentialTerminal", "potential_point", "Down")]
    [InlineData("PCP", "PotentialTerminal", "potential_point", "Left")]
    [InlineData("PCP", "PotentialTerminal", "potential_point", "Right")]
    public void Node_RequiredDirections_SelectTheActualT1Variant(string name, string type, string kind, params string[] directions)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Pins.Clear();
        fixture.Scene.Page.Placements.Clear();
        fixture.Symbols.Clear();
        fixture.Symbols.Add(ConnectionCreateFixture.Variant(name, type, 7, directions));
        fixture.Scene.SymbolBounds[name + "/7"] = [-1, -1, 1, 1];
        fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node(kind, [40, 40], directions));
        JObject result = fixture.Plan();
        Assert.Equal((name, 7), ((string?)result["symbols"]![0]!["symbol"]!["name"], (int)result["symbols"]![0]!["symbol"]!["variant"]!));
        Assert.Empty((JArray)result["logicalAfterGenerate"]!);
    }

    [Fact]
    public void Input_FiftyItems_AcceptsUpperLimitWithoutImplicitBatching()
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Pins.Clear();
        fixture.Scene.Page.Placements.Clear();
        fixture.Request["items"] = new JArray(Enumerable.Range(0, 50).Select(i => ConnectionCreateFixture.Node("potential_point", [8 + i % 10 * 4, 8 + i / 10 * 4], "Up")));
        Assert.Equal(50, ((JArray)fixture.Plan()["symbols"]!).Count);
    }

    [Fact]
    public void Input_Over256KiB_RejectsBeforeSymbolDiscovery()
    {
        var fixture = new ConnectionCreateFixture();
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => ConnectionCreatePlanner.Plan(fixture.Request + new string(' ', 256 * 1024), fixture.Scene, fixture));
        Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        Assert.Equal(0, fixture.Reads);
    }

    [Fact]
    public void Coordinates_FractionalGridInFrenchCulture_RemainInvariant()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var fixture = new ConnectionCreateFixture();
            fixture.Scene.Page.GridSize = 0.5;
            fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("potential_point", [40.5, 40.5], "Up"));
            Assert.Equal([40.5, 40.5], fixture.Plan()["symbols"]![0]!["position"]!.Values<double>());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void Invalid(ConnectionCreateFixture fixture, string pointer)
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => fixture.Plan());
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (ex.Error.Reason, ex.Error.Execution));
        Assert.Contains(pointer, ex.Error.Metadata!["violations"], StringComparison.Ordinal);
    }
}

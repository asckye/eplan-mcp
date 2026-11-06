using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class PageReviewEvidenceTests
{
    [Fact]
    public void ConnectionWithoutVerifiedRoute_DoesNotPresentEndpointChordAsDrawnPath()
    {
        var wire = new CableDefinitionPlanner.Wire { Points = [[120, 220], [120, 100]] };
        JObject connection = EplanPageReviewReader.ConnectionRow(wire, null, null, []);
        Assert.Equal(JTokenType.Null, connection["drawnPath"]!.Type);
    }

    [Theory]
    [InlineData("1.5", "PE", "6")]
    [InlineData("", "", "")]
    public void ConnectionReviewFields_PreserveRawTextAndEmptyStrings(string crossSection, string potentialName, string potentialType)
    {
        JObject connection = EplanPageReviewReader.ConnectionRow(new CableDefinitionPlanner.Wire(), null, null, [], crossSection, potentialName, potentialType);

        Assert.Equal(crossSection, (string?)connection["crossSection"]);
        Assert.Equal(potentialName, (string?)connection["potentialName"]);
        Assert.Equal(potentialType, (string?)connection["potentialType"]);
    }

    [Fact]
    public void AcceptanceCableWithMembers_SelectsOnlyItsConnections_UsingPublishedLocatorNames()
    {
        var wire = new CableDefinitionPlanner.Wire
        {
            Locator = new() { Project = "LargeWrite", Page = "=GAA+A1&EFS1/3", Type = 26, Oid = 42, Digest = new string('a', 64) },
            Points = [[120, 220], [120, 100]],
            Label = "1",
            Colour = "RD",
        };
        JObject connection = EplanPageReviewReader.ConnectionRow(wire, JObject.Parse("""{"device":"-A","pin":"1"}"""), JObject.Parse("""{"device":"-B","pin":"2"}"""), [7]);
        wire.Locator = new() { Oid = 43 };
        var rows = new JArray(connection, EplanPageReviewReader.ConnectionRow(wire, null, null, []));
        var locator = new DrawingLocator { Project = "LargeWrite", Page = "=GAA+A1&EFS1/3", Type = 23, Oid = 100, Digest = new string('b', 64) };
        JObject cable = EplanPageReviewReader.CableRow("+A1-W1", locator, [42, 42], rows);
        Assert.Equal(["project", "page", "type", "oid", "digest"], ((JObject)cable["locator"]!).Properties().Select(p => p.Name));
        JToken core = Assert.Single(cable["cores"]!);
        Assert.Equal(42, (long)core["locator"]!["oid"]!);
        Assert.Equal([7L], core["definitionPoints"]!.Values<long>());
        Assert.Equal("RD", (string?)core["colour"]);
        Assert.True(JToken.DeepEquals(connection, core));
        Assert.NotSame(connection, core);
        Assert.Equal(2, rows.Count);
        Assert.Empty(EplanPageReviewReader.CableRow("+A1-W2", locator, [], rows)["cores"]!);
    }
}

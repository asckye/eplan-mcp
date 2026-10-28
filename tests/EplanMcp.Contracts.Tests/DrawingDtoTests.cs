using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class DrawingDtoTests
{
    [Fact]
    public void TextCreation_BothSerializers_PreserveGeometryAndExactLanguageKeys()
    {
        var request = new PlacementTextCreateArgs
        {
            Project = "Line1",
            Page = "P",
            Text = new() { Position = [12.5, 22.5], HeightMm = 3.5, Contents = new() { ["??_??"] = "Panel", ["de_DE"] = "Feld" } },
        };
        var host = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var bridge = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        string json = System.Text.Json.JsonSerializer.Serialize(request, host);
        PlacementTextCreateArgs fromHost = JsonConvert.DeserializeObject<PlacementTextCreateArgs>(json, bridge)!;
        string bridgeJson = JsonConvert.SerializeObject(fromHost, bridge);
        PlacementTextCreateArgs fromBridge = System.Text.Json.JsonSerializer.Deserialize<PlacementTextCreateArgs>(bridgeJson, host)!;
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(fromBridge, host));
        var result = new PlacementTextCreateData { Text = fromBridge.Text, Warning = "Undo warning" };
        PlacementTextCreateData returned = System.Text.Json.JsonSerializer.Deserialize<PlacementTextCreateData>(JsonConvert.SerializeObject(result, bridge), host)!;
        Assert.Equal("Undo warning", returned.Warning);
        Assert.Equal(request.Text.Contents, returned.Text.Contents);
        Assert.Equal(request.Text.Position, returned.Text.Position);
        Assert.Equal(request.Text.HeightMm, returned.Text.HeightMm);
    }

    [Fact]
    public void DrawingGeometry_BothSerializers_PreserveNullableBoundsAndStableIdentity()
    {
        var locator = new DrawingLocator { Project = "project", Page = "+A1/1", Type = 17, Oid = long.MaxValue, Digest = new string('a', 64) };
        var geometry = new PageDrawingGeometry
        {
            Page = "+A1/1",
            GridSize = 4,
            PageSize = null,
            PlotFrameSize = [420, 297],
            Placements = [new() { Locator = locator, Parent = locator, Kind = "PropertyPlacement", Location = [104, 106], Bounds = null }],
        };
        var host = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var bridge = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        string hostJson = System.Text.Json.JsonSerializer.Serialize(geometry, host);
        string bridgeJson = JsonConvert.SerializeObject(geometry, bridge);

        using JsonDocument left = JsonDocument.Parse(hostJson);
        using JsonDocument right = JsonDocument.Parse(bridgeJson);
        Assert.True(JsonElement.DeepEquals(left.RootElement, right.RootElement));
        PageDrawingGeometry fromHost = JsonConvert.DeserializeObject<PageDrawingGeometry>(hostJson, bridge)!;
        PageDrawingGeometry fromBridge = System.Text.Json.JsonSerializer.Deserialize<PageDrawingGeometry>(bridgeJson, host)!;
        Assert.Equal(hostJson, System.Text.Json.JsonSerializer.Serialize(fromHost, host));
        Assert.Equal(hostJson, System.Text.Json.JsonSerializer.Serialize(fromBridge, host));
        Assert.DoesNotContain("handle", hostJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("databaseIdentifier", hostJson, StringComparison.OrdinalIgnoreCase);
    }
}

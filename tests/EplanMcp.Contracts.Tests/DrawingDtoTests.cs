using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class DrawingDtoTests
{
    [Fact]
    public void GraphicsCreation_BothSerializers_PreserveKindSpecificGeometryAndImageSource()
    {
        var request = new PlacementTextCreateArgs
        {
            Project = "Line1",
            Page = "P",
            Items =
            [
                new() { Kind = "polyline", Position = [10, 20], Points = [[10, 20], [30, 40], [50, 20]], Closed = true, Layer = 108, Pen = new() { ["color"] = -16002, ["width_mm"] = -16002, ["style"] = -16002, ["pattern_length"] = -16002, ["line_end"] = 2 } },
                new() { Kind = "ellipse", Position = [50, 50], RadiusXmm = 1.43748, RadiusYmm = 1.43748, Angle = 90 },
                new() { Kind = "arc", Position = [50, 50], RadiusMm = 8, StartAngle = 30, EndAngle = 135 },
                new() { Kind = "path_text", Position = [30, 50], HeightMm = 3.5, Angle = 90, Alignment = "BaseLineCenter", Contents = new() { ["??_??"] = "Motor circuit" } },
                new() { Kind = "image", Position = [60, 60], EndPosition = [70, 70], File = "fixtures/image.png", FilePath = "resolved-image.png", Sha256 = new('a', 64) }
            ]
        };
        var host = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var bridge = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        string json = System.Text.Json.JsonSerializer.Serialize(request, host);
        PlacementTextCreateArgs fromHost = JsonConvert.DeserializeObject<PlacementTextCreateArgs>(json, bridge)!;
        string bridgeJson = JsonConvert.SerializeObject(fromHost, bridge);
        PlacementTextCreateArgs fromBridge = System.Text.Json.JsonSerializer.Deserialize<PlacementTextCreateArgs>(bridgeJson, host)!;
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(fromBridge, host));
        Assert.Equal(["polyline", "ellipse", "arc", "path_text", "image"], fromBridge.Items!.Select(i => i.Kind));
        Assert.True(fromBridge.Items![0].Closed);
        Assert.Equal(108, fromBridge.Items![0].Layer);
        Assert.Equal(-16002, fromBridge.Items![0].Pen!["pattern_length"]);
        Assert.Equal("BaseLineCenter", fromBridge.Items![3].Alignment);
        Assert.Equal(90, fromBridge.Items![3].Angle);
        Assert.Null(fromBridge.Items![1].Pen);
        Assert.Null(fromBridge.Items![1].Layer);
        Assert.Null(fromBridge.Items![1].Alignment);
    }

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

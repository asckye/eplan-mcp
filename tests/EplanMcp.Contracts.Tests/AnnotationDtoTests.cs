using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class AnnotationDtoTests
{
    [Fact]
    public void AnnotationRequestsAndComputedResultsSurviveBothPipeSerializers()
    {
        var request = new PlacementTextCreateArgs
        {
            Project = "P",
            Page = "1",
            Annotations = true,
            Items = [new() { Kind = "hyperlink", Link = "https://example.test", Position = [20, 30], Contents = new() { ["??_??"] = "Details" } },
                new() { Kind = "dimension", DimensionType = "circle", Arc = new() { Project = "P", Page = "1", Type = 4, Oid = 1, Digest = new('a', 64) } },
                new() { Kind = "placeholder", Name = "A", Records = ["R"], Variables = ["V"] }]
        };
        var bridge = JsonConvert.DeserializeObject<PlacementTextCreateArgs>(System.Text.Json.JsonSerializer.Serialize(request, JsonSerializerOptions.Web))!;
        var host = System.Text.Json.JsonSerializer.Deserialize<PlacementTextCreateArgs>(JsonConvert.SerializeObject(bridge))!;
        Assert.True(host.Annotations);
        Assert.Equal("https://example.test", host.Items![0].Link);
        Assert.Equal("Details", host.Items[0].Contents["??_??"]);
        Assert.Equal(1, host.Items[1].Arc!.Oid);
        Assert.Equal("R", Assert.Single(host.Items[2].Records!));
        Assert.Equal("V", Assert.Single(host.Items[2].Variables!));
        var result = new PlacementTextCreateData { Items = [new() { Kind = "dimension", DimensionText = ["60 mm", "45°"] }] };
        var decoded = System.Text.Json.JsonSerializer.Deserialize<PlacementTextCreateData>(JsonConvert.SerializeObject(result))!;
        Assert.Equal(result.Items[0].DimensionText, decoded.Items[0].DimensionText);
    }
}

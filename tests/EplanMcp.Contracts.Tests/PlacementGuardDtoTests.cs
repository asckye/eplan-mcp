using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class PlacementGuardDtoTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("refuse")]
    [InlineData("warn")]
    public void GuardSurvivesBothPipeSerializers(string? guard)
    {
        var request = new PlacementTextCreateArgs { Project = "P", Page = "1", LayoutGuard = guard };
        var bridge = JsonConvert.DeserializeObject<PlacementTextCreateArgs>(System.Text.Json.JsonSerializer.Serialize(request, JsonSerializerOptions.Web))!;
        var host = System.Text.Json.JsonSerializer.Deserialize<PlacementTextCreateArgs>(JsonConvert.SerializeObject(bridge))!;
        Assert.Equal(guard, host.LayoutGuard);
    }
}

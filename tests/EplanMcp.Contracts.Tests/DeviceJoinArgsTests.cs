using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class DeviceJoinArgsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinDevice_PreservesOmittedAndExplicitValuesAcrossPipeSerializers(bool? join)
    {
        var args = new DevicePlaceArgs { Library = "IEC_symbol", Symbol = "K", Tag = "-K2", JoinDevice = join };
        DevicePlaceArgs bridge = JsonConvert.DeserializeObject<DevicePlaceArgs>(System.Text.Json.JsonSerializer.Serialize(args, JsonSerializerOptions.Web))!;
        DevicePlaceArgs host = System.Text.Json.JsonSerializer.Deserialize<DevicePlaceArgs>(JsonConvert.SerializeObject(bridge))!;
        Assert.Equal(join, bridge.JoinDevice);
        Assert.Equal(join, host.JoinDevice);
        Assert.Null(new DevicePlaceArgs().JoinDevice);
    }
}

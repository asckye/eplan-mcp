using System.Text.Json;
using Newtonsoft.Json;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class DeviceUpdateDtoTests
{
    [Fact]
    public void PartAssignmentArgumentsRoundTripAcrossPipeSerializers()
    {
        var args = new DeviceUpdateArgs { Project = "Line1", Tag = "=GAA+A1-SF91", Part = "SIE.3SB3400-0B", Variant = "1" };
        DeviceUpdateArgs bridge = JsonConvert.DeserializeObject<DeviceUpdateArgs>(System.Text.Json.JsonSerializer.Serialize(args, JsonSerializerOptions.Web))!;
        Assert.Equal((string.Empty, args.Part, args.Variant), (bridge.NewTag, bridge.Part, bridge.Variant));
        DeviceUpdateArgs host = System.Text.Json.JsonSerializer.Deserialize<DeviceUpdateArgs>(JsonConvert.SerializeObject(bridge))!;
        Assert.Equal((args.Part, args.Variant), (host.Part, host.Variant));
    }

    [Fact]
    public void AppendedPartResultRoundTripsAcrossPipeSerializers()
    {
        var data = new DeviceUpdateData { Part = "SIE.3SB3400-0B", Variant = "1" };
        DeviceUpdateData bridge = JsonConvert.DeserializeObject<DeviceUpdateData>(System.Text.Json.JsonSerializer.Serialize(data, JsonSerializerOptions.Web))!;
        DeviceUpdateData host = System.Text.Json.JsonSerializer.Deserialize<DeviceUpdateData>(JsonConvert.SerializeObject(bridge))!;
        Assert.Equal((data.Part, data.Variant), (host.Part, host.Variant));
        Assert.Null(new DeviceUpdateData().Part);
    }
}

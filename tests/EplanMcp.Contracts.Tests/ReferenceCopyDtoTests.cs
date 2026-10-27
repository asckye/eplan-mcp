using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class ReferenceCopyDtoTests
{
    [Fact]
    public void LifecycleDtos_RoundTripAcrossBothSerializers()
    {
        var args = new ReferencePrepareArgs { Source = @"C:\samples\Demo.elk", Destination = @"C:\scratch\copy\Demo.elk", ScratchRoot = @"C:\scratch" };
        ReferencePrepareArgs read = RoundTrip(args);
        Assert.Equal((args.Source, args.Destination, args.ScratchRoot), (read.Source, read.Destination, read.ScratchRoot));
        var copy = new ReferenceCopyData { Handle = new string('a', 32), Path = args.Destination, DatabaseVersion = "18.0", CurrentBefore = "Engineer", CurrentAfter = "Engineer" };
        ReferenceCopyData result = RoundTrip(copy);
        Assert.Equal((copy.Handle, copy.Path, copy.DatabaseVersion, copy.CurrentBefore, copy.CurrentAfter), (result.Handle, result.Path, result.DatabaseVersion, result.CurrentBefore, result.CurrentAfter));
        Assert.Equal(copy.Handle, RoundTrip(new ReferenceCloseArgs { Handle = copy.Handle }).Handle);
        Assert.Null(RoundTrip(new ReferenceCopyData()).CurrentBefore);
    }

    private static T RoundTrip<T>(T value)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        string host = System.Text.Json.JsonSerializer.Serialize(value, options);
        T bridge = JsonConvert.DeserializeObject<T>(host, settings)!;
        return System.Text.Json.JsonSerializer.Deserialize<T>(JsonConvert.SerializeObject(bridge, settings), options)!;
    }
}

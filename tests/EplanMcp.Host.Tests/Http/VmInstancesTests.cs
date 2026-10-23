using System.Globalization;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Http;
using EplanMcp.Host.Tests.Bridge;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Small")]
public sealed class VmInstancesTests
{
    [Theory]
    [InlineData("vm-instances --format json", true)]
    [InlineData("vm-instances", false)]
    [InlineData("vm-instances --format text", false)]
    [InlineData("vm-instances --format json extra", false)]
    public void Command_OnlyAcceptsDocumentedJsonShape(string line, bool valid)
    {
        CliArguments? cli = CliArguments.Parse(line.Split(' '));
        if (valid)
        { Assert.Equal(new CliArguments(CliCommand.VmInstances, Json: true), cli); }
        else
        { Assert.Null(cli); }
    }

    [Fact]
    public async Task Listing_UsesOnlyDiscoveryAndReturnsSortedDistinctPidsWithoutPipeNames()
    {
        var discovery = new FakeDiscovery();
        discovery.Endpoints.AddRange([new BridgeEndpoint("pipe-b", 200), new BridgeEndpoint("pipe-a", 100), new BridgeEndpoint("pipe-a2", 100)]);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(0, await VmInstances.WriteAsync(discovery, output));
        Assert.Equal("{\"schemaVersion\":1,\"processes\":[100,200]}" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Listing_NoEplan_ReturnsAnEmptyList()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(0, await VmInstances.WriteAsync(new FakeDiscovery(), output));
        Assert.Equal("{\"schemaVersion\":1,\"processes\":[]}" + Environment.NewLine, output.ToString());
    }
}

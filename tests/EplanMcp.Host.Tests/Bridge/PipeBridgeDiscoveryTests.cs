using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Bridge;

[Trait("Size", "Small")]
public sealed class PipeBridgeDiscoveryTests
{
    [Theory]
    [InlineData("EplanMcp.Bridge.1234.0123456789abcdef", 1234)]
    [InlineData("EplanMcp.Bridge.7.x", 7)]
    public void Parse_BridgePipeName_ReturnsTheProcessId(string name, int processId)
    {
        Assert.Equal(new BridgeEndpoint(name, processId), PipeBridgeDiscovery.Parse(name, BridgeProtocol.PipePrefix));
    }

    [Theory]
    [InlineData("EplanMcp.Bridge.1234")]
    [InlineData("EplanMcp.Bridge.1234.")]
    [InlineData("EplanMcp.Bridge..abc")]
    [InlineData("EplanMcp.Bridge.-1.abc")]
    [InlineData("EplanMcp.Bridge.x1.abc")]
    [InlineData("eplanmcp.bridge.1.abc")]
    [InlineData("OtherApp.1.abc")]
    public void Parse_OtherNames_AreIgnored(string name)
    {
        Assert.Null(PipeBridgeDiscovery.Parse(name, BridgeProtocol.PipePrefix));
    }
}

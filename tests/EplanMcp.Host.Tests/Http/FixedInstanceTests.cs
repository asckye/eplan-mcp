using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tests.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Small")]
public sealed class FixedInstanceTests
{
    [Fact]
    public async Task FixedProcess_IgnoresClientSelectionAndNeverSwitchesAfterExit()
    {
        var discovery = new FakeDiscovery();
        discovery.Endpoints.AddRange([new BridgeEndpoint("a", 100), new BridgeEndpoint("b", 200)]);
        var connector = new FakeConnector(e => new FakeChannel(e));
        await using var client = new BridgeClient(discovery, connector, NullLogger<BridgeClient>.Instance) { FixedProcessId = 100 };
        HandshakeResult selected = await client.SelectAsync(200, TestContext.Current.CancellationToken);
        Assert.Equal(100, selected.Eplan.ProcessId);
        Assert.Equal(100, (await client.GetStatusAsync(TestContext.Current.CancellationToken)).SelectedProcessId);
        discovery.Endpoints.RemoveAt(0);
        BridgeStatus gone = await client.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Null(gone.Handshake);
        Assert.NotNull(gone.Problem);
        Assert.Equal(100, gone.SelectedProcessId);
        Assert.Equal(100, Assert.Single(connector.Opened).Endpoint.ProcessId);
    }
}

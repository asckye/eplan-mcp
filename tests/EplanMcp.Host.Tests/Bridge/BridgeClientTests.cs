using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

[Trait("Size", "Small")]
public sealed class BridgeClientTests
{
    private static readonly BridgeEndpoint _eplanA = new("EplanMcp.Bridge.100.aaaa", 100);

    private readonly FakeDiscovery _discovery = new();
    private readonly FakeConnector _connector = new(e => new FakeChannel(e));

    [Fact]
    public async Task NoEplan_StatusReportsNotConnectedAndInvokeThrowsIt()
    {
        await using BridgeClient client = Client();

        BridgeStatus status = await client.GetStatusAsync(Token);
        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));

        Assert.Equal(ErrorReason.EplanNotConnected, status.Problem!.Reason);
        Assert.Null(status.Handshake);
        Assert.Equal(ErrorReason.EplanNotConnected, ex.Error.Reason);
        Assert.True(ex.Error.Retryable);
        Assert.Empty(_connector.Opened);
    }

    [Fact]
    public async Task OneEplan_HandshakesOnceAndReusesTheChannel()
    {
        _discovery.Endpoints.Add(_eplanA);
        await using BridgeClient client = Client();

        BridgeStatus status = await client.GetStatusAsync(Token);
        await Invoke(client)();
        await Invoke(client)();

        Assert.Equal(100, status.Handshake!.Eplan.ProcessId);
        FakeChannel channel = Assert.Single(_connector.Opened);
        Assert.Equal([BridgeProtocol.HandshakeMethod, BridgeProtocol.InvokeMethod, BridgeProtocol.InvokeMethod], channel.Methods);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private BridgeClient Client(TimeSpan? queueTimeout = null, TimeSpan? callTimeout = null) =>
        new(_discovery, _connector, NullLogger<BridgeClient>.Instance, queueTimeout ?? TimeSpan.FromSeconds(5), callTimeout ?? TimeSpan.FromSeconds(5));

    private static Func<Task> Invoke(BridgeClient client) =>
        () => client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new ProjectListArgs(), Token);
}

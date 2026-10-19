using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>Choosing one of several EPLAN instances (docs/tools/eplan_session_select.md, section 5).</summary>
[Trait("Size", "Small")]
public sealed class BridgeClientSelectionTests
{
    private static readonly BridgeEndpoint _eplanA = new("EplanMcp.Bridge.100.aaaa", 100);
    private static readonly BridgeEndpoint _eplanB = new("EplanMcp.Bridge.200.bbbb", 200);
    private static readonly BridgeEndpoint _eplanC = new("EplanMcp.Bridge.300.cccc", 300);

    private readonly FakeDiscovery _discovery = new();
    private FakeConnector _connector = new(e => new FakeChannel(e));

    [Fact]
    public async Task Select_OneOfTwo_ConnectsToItAndEveryLaterCallUsesIt()
    {
        _discovery.Endpoints.AddRange([_eplanA, _eplanB]);
        await using BridgeClient client = Client();

        HandshakeResult handshake = await client.SelectAsync(200, Token);
        await Invoke(client);
        BridgeStatus status = await client.GetStatusAsync(Token);

        Assert.Equal(200, handshake.Eplan.ProcessId);
        FakeChannel channel = Assert.Single(_connector.Opened);
        Assert.Equal(_eplanB, channel.Endpoint);
        Assert.Equal([BridgeProtocol.HandshakeMethod, BridgeProtocol.InvokeMethod], channel.Methods);
        Assert.Equal((200, (BridgeError?)null, 200), (status.Handshake!.Eplan.ProcessId, status.Problem, status.SelectedProcessId));
    }

    [Fact]
    public async Task Select_AnotherInstance_SwitchesAndDisposesTheOldChannel()
    {
        _discovery.Endpoints.AddRange([_eplanA, _eplanB]);
        await using BridgeClient client = Client();
        await client.SelectAsync(200, Token);

        await client.SelectAsync(100, Token);
        await Invoke(client);

        Assert.Equal([_eplanB, _eplanA], _connector.Opened.Select(c => c.Endpoint));
        Assert.True(_connector.Opened[0].Disposed);
        Assert.Contains(BridgeProtocol.InvokeMethod, _connector.Opened[1].Methods);
    }

    [Fact]
    public async Task Select_UnknownProcess_IsInvalidArgumentWithTheCandidatesAndKeepsThePreviousChoice()
    {
        _discovery.Endpoints.AddRange([_eplanA, _eplanB]);
        await using BridgeClient client = Client();
        await client.SelectAsync(200, Token);

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(() => client.SelectAsync(999, Token));
        await Invoke(client);

        Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        Assert.Equal("100,200", ex.Error.Metadata!["processIds"]);
        Assert.Contains("'process_id' 999", ex.Error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Equal(_eplanB, Assert.Single(_connector.Opened).Endpoint);
    }

    [Fact]
    public async Task Select_HandshakeRejected_IsNotRemembered()
    {
        _discovery.Endpoints.AddRange([_eplanA, _eplanB]);
        _connector = new FakeConnector(e => e == _eplanB
            ? new FakeChannel(e) { Handshake = (_, _, _) => throw FakeChannel.BusinessError(ErrorReason.BridgeIncompatible) }
            : new FakeChannel(e));
        await using BridgeClient client = Client();

        BridgeCallException rejected = await Assert.ThrowsAsync<BridgeCallException>(() => client.SelectAsync(200, Token));
        BridgeCallException afterwards = await Assert.ThrowsAsync<BridgeCallException>(() => Invoke(client));

        Assert.Equal(ErrorReason.BridgeIncompatible, rejected.Error.Reason);
        Assert.Equal(ErrorReason.SessionNotSelected, afterwards.Error.Reason);
        Assert.Null((await client.GetStatusAsync(Token)).SelectedProcessId);
    }

    [Fact]
    public async Task SelectedEplanExits_TheChoiceIsForgottenAndTwoOthersAreSessionNotSelectedAgain()
    {
        _discovery.Endpoints.AddRange([_eplanA, _eplanB]);
        await using BridgeClient client = Client();
        await client.SelectAsync(200, Token);

        _discovery.Endpoints.Remove(_eplanB);
        _discovery.Endpoints.Add(_eplanC);
        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(() => Invoke(client));
        BridgeStatus status = await client.GetStatusAsync(Token);

        Assert.Equal(ErrorReason.SessionNotSelected, ex.Error.Reason);
        Assert.Contains("eplan_session_select", ex.Error.Message, StringComparison.Ordinal);
        Assert.Null(status.SelectedProcessId);
        Assert.True(_connector.Opened[0].Disposed);
    }

    [Fact]
    public async Task SelectedEplanExits_AndOneOtherRuns_ThatOneIsUsed()
    {
        _discovery.Endpoints.AddRange([_eplanA, _eplanB]);
        await using BridgeClient client = Client();
        await client.SelectAsync(200, Token);

        _discovery.Endpoints.Remove(_eplanB);
        await Invoke(client);

        Assert.Equal(_eplanA, _connector.Opened[^1].Endpoint);
        Assert.Null((await client.GetStatusAsync(Token)).SelectedProcessId);
    }

    [Fact]
    public async Task Select_TheOnlyInstance_Succeeds()
    {
        _discovery.Endpoints.Add(_eplanA);
        await using BridgeClient client = Client();

        HandshakeResult handshake = await client.SelectAsync(100, Token);

        Assert.Equal(100, handshake.Eplan.ProcessId);
    }

    [Fact]
    public async Task Select_NoEplan_IsNotConnected()
    {
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(() => client.SelectAsync(100, Token));

        Assert.Equal(ErrorReason.EplanNotConnected, ex.Error.Reason);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private BridgeClient Client() =>
        new(_discovery, _connector, NullLogger<BridgeClient>.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    private static Task<ProjectListData> Invoke(BridgeClient client) =>
        client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new ProjectListArgs(), Token);
}

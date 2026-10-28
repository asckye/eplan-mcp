using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

[Trait("Size", "Small")]
public sealed class BridgeClientTests
{
    private static readonly BridgeEndpoint _eplanA = new("EplanMcp.Bridge.100.aaaa", 100);
    private static readonly BridgeEndpoint _eplanB = new("EplanMcp.Bridge.200.bbbb", 200);

    private readonly FakeDiscovery _discovery = new();
    private FakeConnector _connector = new(e => new FakeChannel(e));

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

    [Theory]
    [MemberData(nameof(DiscoveryFailures))]
    public async Task DiscoveryFails_IsNotConnectedInsteadOfAnEscapedException(Exception failure)
    {
        _discovery.Failure = failure;
        await using BridgeClient client = Client();

        BridgeStatus status = await client.GetStatusAsync(Token);
        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));

        Assert.Equal(ErrorReason.EplanNotConnected, status.Problem!.Reason);
        Assert.Equal(ErrorReason.EplanNotConnected, ex.Error.Reason);
    }

    // Win32Exception: an elevated EPLAN or another user's EPLAN denies process access.
    public static TheoryData<Exception> DiscoveryFailures() => [new System.ComponentModel.Win32Exception(5), new IOException(), new UnauthorizedAccessException()];

    [Fact]
    public async Task TwoEplans_AreSessionNotSelectedWithBothProcessIds()
    {
        _discovery.Endpoints.AddRange([_eplanA, _eplanB]);
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));
        BridgeStatus status = await client.GetStatusAsync(Token);

        Assert.Equal(ErrorReason.SessionNotSelected, ex.Error.Reason);
        Assert.Equal("100,200", ex.Error.Metadata!["processIds"]);
        Assert.Equal(2, status.Instances.Count);
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

    [Fact]
    public async Task RefreshStatus_RehandshakesAndObservesLicenceLossInsteadOfTheCachedValue()
    {
        _discovery.Endpoints.Add(_eplanA);
        bool? licence = true;
        _connector = new FakeConnector(e => new FakeChannel(e)
        {
            Handshake = (_, _, _) =>
            {
                JsonNode? answer = FakeChannel.CompatibleHandshake(e.ProcessId);
                answer!["eplan"]!["apiExtensionLicence"] = JsonValue.Create(licence);
                return Task.FromResult<JsonNode?>(answer);
            }
        });
        await using BridgeClient client = Client();
        Assert.True((await client.GetStatusAsync(Token)).Handshake!.Eplan.ApiExtensionLicence);
        licence = false;
        Assert.True((await client.GetStatusAsync(Token)).Handshake!.Eplan.ApiExtensionLicence);
        Assert.False((await client.GetStatusAsync(true, Token)).Handshake!.Eplan.ApiExtensionLicence);
        Assert.Equal(2, _connector.Opened.Count);
        Assert.True(_connector.Opened[0].Disposed);
        licence = null;
        Assert.Null((await client.GetStatusAsync(true, Token)).Handshake!.Eplan.ApiExtensionLicence);
    }

    [Fact]
    public async Task EplanRestarted_NewPipeIsUsedAndTheOldChannelDisposed()
    {
        _discovery.Endpoints.Add(_eplanA);
        await using BridgeClient client = Client();
        await Invoke(client)();

        _discovery.Endpoints[0] = _eplanB;
        await Invoke(client)();

        Assert.Equal(2, _connector.Opened.Count);
        Assert.True(_connector.Opened[0].Disposed);
        Assert.Equal(_eplanB, _connector.Opened[1].Endpoint);
    }

    [Fact]
    public async Task BridgeRejectsHandshake_ReportsItsReasonAndDisposesTheChannel()
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector = new FakeConnector(e => new FakeChannel(e) { Handshake = (_, _, _) => throw FakeChannel.BusinessError(ErrorReason.BridgeIncompatible) });
        await using BridgeClient client = Client();

        BridgeStatus status = await client.GetStatusAsync(Token);

        Assert.Equal(ErrorReason.BridgeIncompatible, status.Problem!.Reason);
        Assert.True(_connector.Opened[0].Disposed);
    }

    [Fact]
    public async Task BridgeAnswersWithAnUnsupportedVersion_IsIncompatible()
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector = new FakeConnector(e => new FakeChannel(e) { Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(100, protocolVersion: 99)) });
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));

        Assert.Equal(ErrorReason.BridgeIncompatible, ex.Error.Reason);
        Assert.Contains("install-addin", ex.Error.Message, StringComparison.Ordinal);
        Assert.True(_connector.Opened[0].Disposed);
    }

    [Fact]
    public async Task M1AddIn_ServesProjectListButNotNewerOperations()
    {
        _discovery.Endpoints.Add(_eplanA);
        await using BridgeClient client = Client();

        await Invoke(client)();
        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client, "page.list"));

        Assert.Equal(ErrorReason.BridgeIncompatible, ex.Error.Reason);
        Assert.Equal("page.list", ex.Error.Metadata!["operation"]);
        Assert.Contains("install-addin", ex.Error.Message, StringComparison.Ordinal);
        FakeChannel channel = Assert.Single(_connector.Opened);
        Assert.Equal([BridgeProtocol.HandshakeMethod, BridgeProtocol.InvokeMethod], channel.Methods);
        Assert.False(channel.Disposed);
    }

    [Fact]
    public async Task AddInAnnouncingItsOperations_IsAskedOnlyForThose()
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector = new FakeConnector(e => new FakeChannel(e) { Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(100, operations: ["page.list"])) });
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));
        await Invoke(client, "page.list")();

        Assert.Equal(ErrorReason.BridgeIncompatible, ex.Error.Reason);
        Assert.Equal([BridgeProtocol.HandshakeMethod, BridgeProtocol.InvokeMethod], _connector.Opened[0].Methods);
    }

    [Theory]
    [MemberData(nameof(ConnectFailures))]
    public async Task ConnectFails_IsNotConnected(Exception failure)
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector.Failure = failure;
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));

        Assert.Equal(ErrorReason.EplanNotConnected, ex.Error.Reason);
    }

    public static TheoryData<Exception> ConnectFailures() => [new TimeoutException(), new IOException(), new UnauthorizedAccessException()];

    [Fact]
    public async Task BusinessError_PassesThroughAndKeepsTheChannel()
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector = new FakeConnector(e => new FakeChannel(e, (_, _, _) => throw FakeChannel.BusinessError(ErrorReason.EplanBusy, "dialog open", retryable: true)));
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));

        Assert.Equal(ErrorReason.EplanBusy, ex.Error.Reason);
        Assert.Equal("dialog open", ex.Error.Message);
        Assert.False(Assert.Single(_connector.Opened).Disposed);
    }

    [Fact]
    public async Task ProtocolError_IsInternalWithACorrelationId()
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector = new FakeConnector(e => new FakeChannel(e, (_, _, _) => throw new BridgeRpcException(-32602, "Unknown operation 'x'.", null)));
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));

        Assert.Equal(ErrorReason.Internal, ex.Error.Reason);
        Assert.NotEmpty(ex.Error.Metadata!["correlationId"]);
    }

    [Fact]
    public async Task ConnectionBreaks_IsRetryableConnectionLostAndTheNextCallReconnects()
    {
        _discovery.Endpoints.Add(_eplanA);
        int calls = 0;
        _connector = new FakeConnector(e => new FakeChannel(e, (_, _, _) => ++calls == 1 ? throw new IOException("pipe is broken") : Task.FromResult<JsonNode?>(new JsonObject { ["data"] = new JsonObject() })));
        await using BridgeClient client = Client();

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));
        await Invoke(client)();

        Assert.Equal(ErrorReason.EplanNotConnected, ex.Error.Reason);
        Assert.True(ex.Error.Retryable);
        Assert.Equal(2, _connector.Opened.Count);
        Assert.True(_connector.Opened[0].Disposed);
    }

    [Fact]
    public async Task CallExceedsItsTimeout_IsTimeoutAndDropsTheChannel()
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector = new FakeConnector(e => new FakeChannel(e, async (_, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        }));
        await using BridgeClient client = Client(callTimeout: TimeSpan.FromMilliseconds(50));

        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));

        Assert.Equal(ErrorReason.Timeout, ex.Error.Reason);
        Assert.True(_connector.Opened[0].Disposed);
    }

    [Fact]
    public async Task CallerCancels_PropagatesCancellationAndDropsTheChannel()
    {
        _discovery.Endpoints.Add(_eplanA);
        using var cancel = new CancellationTokenSource();
        _connector = new FakeConnector(e => new FakeChannel(e, async (_, _, token) =>
        {
            await cancel.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return null;
        }));
        await using BridgeClient client = Client();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), cancel.Token));

        Assert.True(_connector.Opened[0].Disposed);
    }

    [Fact]
    public async Task CallerCancels_AnAddInThatCanCancel_IsAskedToStopThatInvokeOverANewChannel()
    {
        _discovery.Endpoints.Add(_eplanA);
        using var cancel = new CancellationTokenSource();
        var invokes = new List<JsonNode>();
        _connector = new FakeConnector(e => new FakeChannel(e, async (_, parameters, token) =>
        {
            invokes.Add(parameters!.DeepClone());
            if (invokes.Count > 1)
            {
                return new JsonObject { ["data"] = new JsonObject { ["found"] = true } };
            }

            await cancel.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return null;
        })
        {
            Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(e.ProcessId, operations: [OperationNames.ProjectList, OperationNames.OperationCancel])),
        });
        await using BridgeClient client = Client();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), cancel.Token));

        Assert.Equal(2, invokes.Count);
        Assert.Equal(OperationNames.OperationCancel, invokes[1]["operation"]!.GetValue<string>());
        Assert.Equal(invokes[0]["correlationId"]!.GetValue<string>(), invokes[1]["args"]!["requestId"]!.GetValue<string>());
        Assert.Equal(2, _connector.Opened.Count);
        Assert.True(_connector.Opened[0].Disposed);
    }

    [Fact]
    public async Task TheProgressOfTheInvoke_ReachesTheCallerAndNoOtherReportDoes()
    {
        _discovery.Endpoints.Add(_eplanA);
        _connector = new FakeConnector(e => new FakeChannel(e, (_, parameters, _) =>
        {
            IProgress<ProgressNotification> progress = _connector.Opened[0].Progress!;
            progress.Report(new ProgressNotification { RequestId = "another", Progress = 9 });
            progress.Report(new ProgressNotification { RequestId = parameters!["correlationId"]!.GetValue<string>(), Progress = 1, Total = 2, Message = "Exporting page 2 of 2" });
            return Task.FromResult<JsonNode?>(new JsonObject { ["data"] = new JsonObject { ["projects"] = new JsonArray() } });
        }));
        var reports = new RecordedReports<ProgressNotification>();
        await using BridgeClient client = Client();

        await client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TimeSpan.FromSeconds(5), reports, Token);

        ProgressNotification report = Assert.Single(reports.Values);
        Assert.Equal((1, 2, "Exporting page 2 of 2"), (report.Progress, report.Total, report.Message));
    }

    [Fact]
    public async Task SecondCallWhileTheFirstHoldsTheQueue_IsRetryableEplanBusy()
    {
        _discovery.Endpoints.Add(_eplanA);
        var release = new TaskCompletionSource();
        _connector = new FakeConnector(e => new FakeChannel(e, async (_, _, _) =>
        {
            await release.Task;
            return new JsonObject { ["data"] = new JsonObject() };
        }));
        await using BridgeClient client = Client(queueTimeout: TimeSpan.FromMilliseconds(50));

        Task first = Invoke(client)();
        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(Invoke(client));
        release.SetResult();
        await first;

        Assert.Equal(ErrorReason.EplanBusy, ex.Error.Reason);
        Assert.True(ex.Error.Retryable);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private BridgeClient Client(TimeSpan? queueTimeout = null, TimeSpan? callTimeout = null) =>
        new(_discovery, _connector, NullLogger<BridgeClient>.Instance, queueTimeout ?? TimeSpan.FromSeconds(5), callTimeout ?? TimeSpan.FromSeconds(5));

    private static Func<Task> Invoke(BridgeClient client, string operation = OperationNames.ProjectList) =>
        () => client.InvokeAsync<ProjectListArgs, ProjectListData>(operation, new ProjectListArgs(), Token);
}

using System.IO.Pipes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// The Host's discovery, connector, framing and System.Text.Json against Bridge.Core's server, framing and
/// Newtonsoft.Json on a real named pipe. Only EPLAN itself is faked (dispatcher and catalog).
/// </summary>
[Trait("Size", "Medium")]
public sealed class HostBridgeInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public HostBridgeInteropTests()
    {
        var router = new OperationRouter(
            [new ProjectListOperation(new Catalog())],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions
            {
                BridgeVersion = "0.1.0-test",
                Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421", Variant = "Pro Panel", ApiExtensionLicence = true },
            },
            router,
            new NullLog());
        _server.Start();
        _client = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task Status_HandshakesWithTheBridge()
    {
        BridgeStatus status = await _client.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Null(status.Problem);
        Assert.Equal(Environment.ProcessId, Assert.Single(status.Instances).ProcessId);
        Assert.Equal("Pro Panel", status.Handshake!.Eplan.Variant);
        Assert.Equal("0.1.0-test", status.Handshake.BridgeVersion);
        Assert.True(status.Handshake.Eplan.ApiExtensionLicence);
    }

    [Fact]
    public async Task ProjectList_CarriesUnicodeProjectDataIntact()
    {
        ProjectListData data = await _client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new ProjectListArgs(), TestContext.Current.CancellationToken);

        Assert.Collection(
            data.Projects,
            p => Assert.Equal(("Line1", true, false), (p.Name, p.Current, p.ReadOnly)),
            p => Assert.Equal(("项目 二", @"D:\项目\二", true), (p.Name, p.Path, p.ReadOnly)));
    }

    [Fact]
    public async Task BridgeGoesAway_NextCallIsNotConnected()
    {
        await _client.GetStatusAsync(TestContext.Current.CancellationToken);

        _server.Dispose();
        BridgeCallException ex = await Assert.ThrowsAsync<BridgeCallException>(() =>
            _client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new ProjectListArgs(), TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.EplanNotConnected, ex.Error.Reason);
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() =>
        [
            new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true },
            new OpenProject { Name = "项目 二", Path = @"D:\项目\二", ReadOnly = true },
        ];
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}

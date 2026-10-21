using System.IO.Pipes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Addin;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>
/// install-addin's look at the Bridge of the EPLAN it started, against Bridge.Core's server on a real pipe under a
/// test prefix, so it never reaches a running EPLAN: the pipe of that process, and the add-in path in the handshake.
/// Medium.
/// </summary>
[Trait("Size", "Medium")]
public sealed class BridgeProbeTests : IDisposable
{
    private const string Dll = @"C:\Users\工程师\EplanMcp\app\bridge\net481\EplanMcp.Bridge.Eplan.dll";

    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly BridgeServer _server;

    public BridgeProbeTests()
    {
        var router = new OperationRouter([], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "0.1.0-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId }, AddinPath = Dll },
            router,
            new NullLog());
        _server.Start();
    }

    private BridgeProbe Probe => new(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector());

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task TheBridgeOfThatProcess_IsUpAndSaysWhereEplanRegisteredIt()
    {
        Assert.True(Probe.IsUp(Environment.ProcessId));

        HandshakeResult? handshake = await Probe.HandshakeAsync(Environment.ProcessId, TestContext.Current.CancellationToken);

        Assert.Equal((Dll, "0.1.0-test"), (handshake?.AddinPath, handshake?.BridgeVersion));
    }

    [Fact]
    public async Task AnotherProcess_HasNoBridge()
    {
        const int Other = 4;

        Assert.False(Probe.IsUp(Other));
        Assert.Null(await Probe.HandshakeAsync(Other, TestContext.Current.CancellationToken));
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout) => DispatchResult.Completed(work());
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}

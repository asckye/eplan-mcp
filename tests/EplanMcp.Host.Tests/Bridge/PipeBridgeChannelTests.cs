using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Tests.Bridge;

public sealed class PipeBridgeChannelTests
{
    private const string ResultFrame = """{"jsonrpc":"2.0","id":1,"result":{"value":"ok"}}""";

    public static TheoryData<string> InvalidMembers => new()
    {
        """{"jsonrpc":"2.0","jsonrpc":"2.0","id":1,"result":{}}""",
        """{"jsonrpc":"2.0","id":1,"id":1,"result":{}}""",
        """{"jsonrpc":"2.0","id":1,"result":{},"result":{}}""",
        """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"error"},"error":{"code":-32000,"message":"error"}}""",
        """{"jsonrpc":"2.0","id":1,"result":{},"extra":true}""",
        """{"jsonrpc":"2.0","id":1,"result":{},"Result":{}}""",
        """{"jsonrpc":"2.0","id":1,"result":{},"params":{}}""",
        """{"jsonrpc":"2.0","id":1,"\u0069d":1,"result":{}}""",
        """{"jsonrpc":"2.0","jsonrpc":"2.0","method":"progress","params":{}}""",
        """{"jsonrpc":"2.0","method":"progress","method":"progress","params":{}}""",
        """{"jsonrpc":"2.0","method":"progress","params":{},"params":{}}""",
        """{"jsonrpc":"2.0","method":"progress","params":{},"extra":true}""",
        """{"jsonrpc":"2.0","method":"progress","params":{},"Params":{}}""",
        """{"jsonrpc":"2.0","method":"progress","params":{},"result":{}}""",
        """{"jsonrpc":"2.0","method":"progress","\u006dethod":"progress","params":{}}""",
        """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"code":-32000,"message":"error"}}""",
        """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"error","message":"error"}}""",
        """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"error","data":{},"data":{}}}""",
        """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"error","extra":true}}""",
        """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"error","Code":-32000}}""",
        """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"\u0063ode":-32000,"message":"error"}}""",
    };

    [Theory]
    [MemberData(nameof(InvalidMembers))]
    [Trait("Size", "Small")]
    public async Task DuplicateOrUnknownProtocolMembers_AreRejected(string frame)
    {
        BridgeRpcException error = await Assert.ThrowsAsync<BridgeRpcException>(() => CallAsync([frame, ResultFrame]));

        Assert.Equal(-32600, error.Code);
    }

    [Fact]
    [Trait("Size", "Small")]
    public async Task NormalResult_PreservesOperationPayload()
    {
        JsonNode? result = await CallAsync(["""{"jsonrpc":"2.0","id":1,"result":{"extra":{"params":1},"value":"ok"}}"""]);

        Assert.Equal("ok", result!["value"]!.GetValue<string>());
        Assert.Equal(1, result["extra"]!["params"]!.GetValue<int>());
    }

    [Fact]
    [Trait("Size", "Small")]
    public async Task NormalBusinessError_PreservesErrorPayload()
    {
        BridgeRpcException error = await Assert.ThrowsAsync<BridgeRpcException>(() => CallAsync(
            ["""{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"error","data":{"extra":{"code":1}}}}"""]));

        Assert.Equal(BridgeProtocol.BusinessErrorCode, error.Code);
        Assert.Equal("error", error.Message);
        Assert.Equal(1, error.ErrorData!["extra"]!["code"]!.GetValue<int>());
    }

    [Fact]
    [Trait("Size", "Small")]
    public async Task ProtocolErrorWithNullId_IsRejectedInsteadOfSkippedAsNotification()
    {
        BridgeRpcException error = await Assert.ThrowsAsync<BridgeRpcException>(() => CallAsync(
            ["""{"jsonrpc":"2.0","id":null,"error":{"code":-32600,"message":"Invalid request. Diagnostic ID: x","data":{"diagnosticId":"x"}}}""", ResultFrame]));

        Assert.Equal(-32600, error.Code);
    }

    [Fact]
    [Trait("Size", "Small")]
    public async Task NormalProgressAndUnknownNotification_PreserveExistingBehavior()
    {
        var reports = new RecordedReports<ProgressNotification>();
        JsonNode? result = await CallAsync(
            [
                """{"jsonrpc":"2.0","method":"future","params":{"extra":true}}""",
                """{"jsonrpc":"2.0","method":"progress","params":{"requestId":"request","progress":2,"total":3,"message":"Step 2","extra":true}}""",
                ResultFrame,
            ], reports);

        Assert.Equal("ok", result!["value"]!.GetValue<string>());
        ProgressNotification report = Assert.Single(reports.Values);
        Assert.Equal(("request", 2, 3, "Step 2"), (report.RequestId, report.Progress, report.Total, report.Message));
    }

    [Fact]
    [Trait("Size", "Medium")]
    public async Task RejectedFrame_IsMappedToProtocolErrorWithLoggedDiagnostic()
    {
        string name = "EplanMcpTest." + Guid.NewGuid().ToString("N");
        using var server = NewServer(name);
        var endpoint = new BridgeEndpoint(name, Environment.ProcessId);
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(endpoint);
        var log = new CaptureLog();
        await using var client = new BridgeClient(discovery, new PipeBridgeConnector(), log);
        Task connected = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        Task<BridgeStatus> statusTask = client.GetStatusAsync(TestContext.Current.CancellationToken);
        await connected.WaitAsync(TestContext.Current.CancellationToken);
        await RespondAsync(server, ["""{"jsonrpc":"2.0","id":1,"result":{},"extra":true}"""]);

        BridgeStatus status = await statusTask;

        BridgeError error = Assert.IsType<BridgeError>(status.Problem);
        Assert.Equal(ErrorReason.Internal, error.Reason);
        string diagnostic = error.Metadata!["correlationId"];
        Assert.True(Guid.TryParseExact(diagnostic, "N", out _));
        Assert.Contains(log.Messages, message => message.Contains(diagnostic, StringComparison.Ordinal)
            && message.Contains("-32600", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{},"extra":true}""")]
    [InlineData("""{"jsonrpc":"2.0","id":null,"error":{"code":-32000,"message":"error","data":{"reason":"EPLAN_BUSY","message":"error"}}}""")]
    [Trait("Size", "Small")]
    public async Task InvalidHandshakeFrame_IsInternalWithLoggedDiagnostic(string frame)
    {
        using var buffer = new MemoryStream();
        await Framing.WriteFrameAsync(buffer, Encoding.UTF8.GetBytes(frame), TestContext.Current.CancellationToken);
        using var stream = new ResponseStream(buffer.ToArray());
        var endpoint = new BridgeEndpoint("unused", 1);
        await using var channel = new PipeBridgeChannel(stream, endpoint);
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(endpoint);
        var log = new CaptureLog();
        await using var client = new BridgeClient(discovery, new FirstChannelConnector(channel), log);

        BridgeStatus status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

        BridgeError error = Assert.IsType<BridgeError>(status.Problem);
        Assert.Equal(ErrorReason.Internal, error.Reason);
        string diagnostic = error.Metadata!["correlationId"];
        Assert.True(Guid.TryParseExact(diagnostic, "N", out _));
        Assert.Contains(log.Messages, message => message.Contains(diagnostic, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"progress\",\"params\":{},\"extra\":true}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{},\"extra\":true}")]
    [Trait("Size", "Small")]
    public async Task RejectedInvokeFrame_DropsChannelBeforeNextCall(string invalidFrame)
    {
        var endpoint = new BridgeEndpoint("unused", 1);
        string handshake = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":" + FakeChannel.CompatibleHandshake(endpoint.ProcessId)!.ToJsonString() + "}";
        using var buffer = new MemoryStream();
        foreach (string frame in new[] { handshake, invalidFrame, """{"jsonrpc":"2.0","id":2,"result":{"data":{"projects":[]}}}""" })
        {
            await Framing.WriteFrameAsync(buffer, Encoding.UTF8.GetBytes(frame), TestContext.Current.CancellationToken);
        }

        using var stream = new ResponseStream(buffer.ToArray());
        await using var rawChannel = new PipeBridgeChannel(stream, endpoint);
        await using var first = new TrackedChannel(rawChannel);
        var connector = new FirstChannelConnector(first);
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(endpoint);
        var log = new CaptureLog();
        await using var client = new BridgeClient(discovery, connector, log);
        Assert.Null((await client.GetStatusAsync(TestContext.Current.CancellationToken)).Problem);

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() =>
            client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.Internal, error.Error.Reason);
        string diagnostic = error.Error.Metadata!["correlationId"];
        Assert.Contains(log.Messages, message => message.Contains(diagnostic, StringComparison.Ordinal));
        Assert.True(first.Disposed);
        await client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, connector.Connections);
    }

    [Fact]
    [Trait("Size", "Small")]
    public async Task FrameAboveExistingLimit_IsRejectedBeforeReadingBody()
    {
        byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + (BridgeProtocol.MaxFrameBytes + 1).ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<IOException>(() => Framing.ReadFrameAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal(header.Length, stream.Position);
    }

    private static async Task<JsonNode?> CallAsync(string[] frames, IProgress<ProgressNotification>? progress = null)
    {
        using var buffer = new MemoryStream();
        foreach (string frame in frames)
        {
            await Framing.WriteFrameAsync(buffer, Encoding.UTF8.GetBytes(frame), TestContext.Current.CancellationToken);
        }

        using var stream = new ResponseStream(buffer.ToArray());
        await using var channel = new PipeBridgeChannel(stream, new BridgeEndpoint("unused", 1));
        return await channel.CallAsync(BridgeProtocol.InvokeMethod, new JsonObject(), progress, TestContext.Current.CancellationToken);
    }

    private static NamedPipeServerStream NewServer(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static async Task RespondAsync(NamedPipeServerStream server, string[] frames)
    {
        await Framing.ReadFrameAsync(server, TestContext.Current.CancellationToken);
        using var buffer = new MemoryStream();
        foreach (string frame in frames)
        {
            await Framing.WriteFrameAsync(buffer, Encoding.UTF8.GetBytes(frame), TestContext.Current.CancellationToken);
        }

        await server.WriteAsync(buffer.ToArray(), TestContext.Current.CancellationToken);
        await server.FlushAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CaptureLog : ILogger<BridgeClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class ResponseStream(byte[] response) : MemoryStream(response, writable: false)
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FirstChannelConnector(IBridgeChannel first) : IBridgeConnector
    {
        public int Connections { get; private set; }

        public Task<IBridgeChannel> ConnectAsync(BridgeEndpoint endpoint, CancellationToken cancellationToken) =>
            Task.FromResult(++Connections == 1 ? first : (IBridgeChannel)new FakeChannel(endpoint));
    }

    private sealed class TrackedChannel(IBridgeChannel inner) : IBridgeChannel
    {
        public BridgeEndpoint Endpoint => inner.Endpoint;

        public bool Disposed { get; private set; }

        public Task<JsonNode?> CallAsync(string method, JsonNode? parameters, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) =>
            inner.CallAsync(method, parameters, progress, cancellationToken);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return inner.DisposeAsync();
        }
    }
}

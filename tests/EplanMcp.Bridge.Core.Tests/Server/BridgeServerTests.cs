using System.IO.Pipes;
using System.Text;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Server;

/// <summary>The real server on a real named pipe, with an inline dispatcher and a fake catalog.</summary>
[Trait("Size", "Medium")]
public sealed class BridgeServerTests : IDisposable
{
    private readonly ListLog _log = new();
    private readonly BridgeServer _server;

    public BridgeServerTests()
    {
        _server = NewServer(CreatePipe);
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    [Fact]
    public void CreatePipeName_HasPrefixPidAndRandomPart()
    {
        string name = BridgeServer.CreatePipeName(1234);

        Assert.Matches(@"^EplanMcp\.Bridge\.1234\.[0-9a-f]{16}$", name);
        Assert.NotEqual(name, BridgeServer.CreatePipeName(1234));
    }

    [Fact]
    public void Start_CreatesThePipeBeforeReturning()
    {
        // The Host discovers Bridges by listing \\.\pipe\ (ADR-0008) and does not wait for a pipe to appear.
        int caller = Environment.CurrentManagedThreadId;
        bool createdByStart = false;
        using BridgeServer server = NewServer(name =>
        {
            if (Environment.CurrentManagedThreadId == caller)
            {
                createdByStart = true;
            }

            return CreatePipe(name);
        });

        server.Start();

        Assert.True(createdByStart);
        Assert.Contains(server.PipeName, Directory.EnumerateFiles(@"\\.\pipe\").Select(Path.GetFileName));
    }

    [Fact]
    public async Task Client_CanHandshakeAndInvokeOverThePipe()
    {
        await using NamedPipeClientStream client = await ConnectAsync();

        JObject handshake = await CallAsync(client, """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1}}""");
        JObject invoke = await CallAsync(client, """{"jsonrpc":"2.0","id":2,"method":"invoke","params":{"operation":"project.list","correlationId":"c","args":{}}}""");

        Assert.Equal(1, (int)handshake["result"]!["protocolVersion"]!);
        Assert.Equal("Line1", (string?)invoke["result"]!["data"]!["projects"]![0]!["name"]);
    }

    [Fact]
    public async Task TwoClients_AreServedAtTheSameTime()
    {
        await using NamedPipeClientStream first = await ConnectAsync();
        await using NamedPipeClientStream second = await ConnectAsync();

        JObject a = await CallAsync(second, """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1}}""");
        JObject b = await CallAsync(first, """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1}}""");

        Assert.NotNull(a["result"]);
        Assert.NotNull(b["result"]);
    }

    [Fact]
    public async Task BrokenFraming_ClosesOnlyThatConnection()
    {
        // The server closes the connection. A pipe reports that as end of stream, or as "pipe is broken" on the
        // read or on the client's own dispose; any other outcome (a reply, an assertion failure) fails the test.
        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            await using NamedPipeClientStream bad = await ConnectAsync();
            await bad.WriteAsync(Encoding.ASCII.GetBytes("Content-Length: x\r\n\r\n"), TestContext.Current.CancellationToken);
            Assert.Null(await Framing.ReadFrameAsync(bad, 1024, TestContext.Current.CancellationToken));
        });
        Assert.True(failure is null or IOException, failure?.ToString());

        await using NamedPipeClientStream good = await ConnectAsync();
        JObject response = await CallAsync(good, """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1}}""");
        Assert.NotNull(response["result"]);
    }

    [Fact]
    public async Task Dispose_StopsListening()
    {
        _server.Dispose();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = new NamedPipeClientStream(".", _server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(500, TestContext.Current.CancellationToken);
        });
    }

    private BridgeServer NewServer(Func<string, NamedPipeServerStream> createPipe) => new(
        "EplanMcp.Test." + Guid.NewGuid().ToString("N"),
        createPipe,
        new BridgeServerOptions { BridgeVersion = "t", Eplan = new EplanInstanceInfo { ProcessId = 1 }, MaxConnections = 2 },
        new OperationRouter(
            [new ProjectListOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "p" }))],
            new InlineDispatcher(),
            null,
            _log,
            TimeSpan.FromSeconds(10)),
        _log);

    private NamedPipeServerStream CreatePipe(string name) =>
        new(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var client = new NamedPipeClientStream(".", _server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000, TestContext.Current.CancellationToken);
        return client;
    }

    private static async Task<JObject> CallAsync(Stream stream, string request)
    {
        await Framing.WriteFrameAsync(stream, Encoding.UTF8.GetBytes(request), TestContext.Current.CancellationToken);
        byte[]? body = await Framing.ReadFrameAsync(stream, 1 << 20, TestContext.Current.CancellationToken);
        return JObject.Parse(Encoding.UTF8.GetString(body!));
    }
}

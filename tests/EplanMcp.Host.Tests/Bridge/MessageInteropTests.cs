using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// eplan_message_list as the Host runs it, through a real BridgeClient and named pipe into Bridge.Core's operation.
/// Only EPLAN's message collection is faked.
/// </summary>
[Trait("Size", "Medium")]
public sealed class MessageInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly Reader _reader = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public MessageInteropTests()
    {
        var router = new OperationRouter([new MessageListOperation(_reader)], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "0.1.0-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
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
    public async Task MessageList_WalksTheNewestFirstWithTheCursorAndReadsEplanOncePerCall()
    {
        _reader.Messages.AddRange(
        [
            new SystemMessageRecord { Level = 2, Index = "S000000", Text = "旧的警告", Occurrences = 1 },
            new SystemMessageRecord { Level = 0, Index = "S000000", Text = "trace", Occurrences = 1 },
            new SystemMessageRecord { Level = 4, Index = "S085016", Text = "File D:\\Projects\\a.elk not found", Occurrences = 3 },
            new SystemMessageRecord { Level = 7, Index = "S000000", Text = "A new version is available", Occurrences = 1 },
        ]);

        var texts = new List<string>();
        string? cursor = null;
        int calls = 0;
        do
        {
            JsonElement page = Structured(await MessageTools.MessageListAsync(_client, SystemMessage.Warning, 1, cursor, Token));
            texts.AddRange(page.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("text").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
            calls++;
        }
        while (cursor is not null);

        Assert.Equal(["File D:\\Projects\\a.elk not found", "旧的警告"], texts);
        Assert.Equal(2, _reader.Reads);
        Assert.Equal(calls, _reader.Reads);
    }

    [Fact]
    public async Task MessageList_CarriesNumberLevelAndOccurrencesOverThePipe()
    {
        _reader.Messages.Add(new SystemMessageRecord { Level = 5, Index = "S085016", Text = "fatal", Occurrences = 2 });

        CallToolResult result = await MessageTools.MessageListAsync(_client, SystemMessage.Error, 20, null, Token);

        JsonElement message = Structured(result).GetProperty("messages")[0];
        Assert.Equal(("fatal", "S085016", 2), (message.GetProperty("level").GetString(), message.GetProperty("number").GetString(), message.GetProperty("occurrences").GetInt32()));
        Assert.Equal("1 message at error level or above.", ((TextContentBlock)result.Content[0]).Text);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value;
    }

    private sealed class Reader : ISystemMessageReader
    {
        public List<SystemMessageRecord> Messages { get; } = [];

        public int Reads { get; private set; }

        public IReadOnlyList<SystemMessageRecord> ReadAll()
        {
            Reads++;
            return [.. Messages];
        }
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

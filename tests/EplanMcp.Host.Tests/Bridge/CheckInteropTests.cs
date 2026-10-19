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
/// eplan_check_list as the Host runs it, through a real BridgeClient and named pipe into Bridge.Core's operation.
/// Only EPLAN is faked: the project catalog and the project's check messages.
/// </summary>
[Trait("Size", "Medium")]
public sealed class CheckInteropTests : IAsyncDisposable
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };

    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly Catalog _catalog = new();
    private readonly Reader _reader = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public CheckInteropTests()
    {
        var router = new OperationRouter([new CheckListOperation(_catalog, _reader)], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
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
    public async Task CheckList_WalksTheWarningsWithTheCursorAndKeepsTheCounts()
    {
        _catalog.Projects = [_line1];
        _reader.Messages.AddRange(
        [
            new ProjectMessageRecord { Id = 4025, Category = "Warning", Region = "Connections", Text = "端子 -X1:1 没有连接", ObjectKind = "Terminal", ObjectName = "=GAA+A1-X1:1" },
            new ProjectMessageRecord { Id = 13001, Category = "Hint", Region = "PartsData", Text = "h" },
            new ProjectMessageRecord { Id = 4026, Category = "Warning", Region = "Connections", Text = "done", Done = true },
            new ProjectMessageRecord { Id = 4027, Category = "Warning", Region = "Cables", Text = "w2" },
        ]);

        var texts = new List<string>();
        string? cursor = null;
        JsonElement page;
        do
        {
            page = Structured(await CheckTools.CheckListAsync(_client, null, CheckMessage.Warning, false, 1, cursor, Token));
            texts.AddRange(page.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("text").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(["端子 -X1:1 没有连接", "w2"], texts);
        Assert.Equal((2, 1), (page.GetProperty("counts").GetProperty("warning").GetInt32(), page.GetProperty("counts").GetProperty("hint").GetInt32()));
        Assert.Equal("Line1", page.GetProperty("project").GetString());
        Assert.Equal([_line1.Path, _line1.Path], _reader.Reads);
    }

    [Fact]
    public async Task CheckList_CarriesTheObjectOverThePipe()
    {
        _catalog.Projects = [_line1];
        _reader.Messages.Add(new ProjectMessageRecord { Id = 7001, Category = "Error", Region = "Devices", Text = "e", ObjectKind = "Function", ObjectName = "=GAA+A1-FC1" });

        CallToolResult result = await CheckTools.CheckListAsync(_client, "Line1", null, false, 50, null, Token);

        JsonElement message = Structured(result).GetProperty("messages")[0];
        Assert.Equal(("Function", "=GAA+A1-FC1"), (message.GetProperty("object").GetProperty("kind").GetString(), message.GetProperty("object").GetProperty("name").GetString()));
        Assert.Equal("1 error from the last check.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task CheckList_NoProjectOpen_IsProjectNotOpen()
    {
        CallToolResult result = await CheckTools.CheckListAsync(_client, null, null, false, 50, null, Token);

        Assert.True(result.IsError);
        JsonElement error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
        Assert.Equal(ErrorReason.ProjectNotOpen, error.GetProperty("reason").GetString());
        Assert.Empty(_reader.Reads);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value;
    }

    private sealed class Catalog : IProjectCatalog
    {
        public OpenProject[] Projects { get; set; } = [];

        public IReadOnlyList<OpenProject> ListOpenProjects() => Projects;
    }

    private sealed class Reader : IProjectMessageReader
    {
        public List<ProjectMessageRecord> Messages { get; } = [];

        public List<string> Reads { get; } = [];

        public IReadOnlyList<ProjectMessageRecord> ReadMessages(string projectPath)
        {
            Reads.Add(projectPath);
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

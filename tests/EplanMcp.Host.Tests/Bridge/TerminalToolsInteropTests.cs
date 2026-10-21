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
/// The terminal strip tools through the real BridgeClient, pipe and Bridge.Core operations; only the EPLAN adapter is
/// fake (<see cref="SampleTerminals"/>).
/// </summary>
[Trait("Size", "Medium")]
public sealed class TerminalToolsInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public TerminalToolsInteropTests()
    {
        var catalog = new Catalog();
        var terminals = new SampleTerminals();
        var router = new OperationRouter(
            [new ProjectListOperation(catalog), new TerminalStripListOperation(catalog, terminals), new TerminalStripGetOperation(catalog, terminals)],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000c3",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "terminals", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
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
    public async Task TerminalStripList_SortsByNameAndPagesThroughEveryStrip()
    {
        var names = new List<string>();
        string? cursor = null;
        do
        {
            JsonElement data = Structured(await List(limit: 3, cursor: cursor));
            names.AddRange(data.GetProperty("strips").EnumerateArray().Select(s => s.GetProperty("name").GetString()!));
            cursor = data.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(["+A1-XD2", "+A1-XD4", "+A2-XD2", "+B2.X1-XZ1"], names);
    }

    [Fact]
    public async Task TerminalStripList_NameContains_KeepsTheMatchingStripsWithTheirCounts()
    {
        CallToolResult result = await List(nameContains: "+a2");

        JsonElement strip = Assert.Single(Structured(result).GetProperty("strips").EnumerateArray());
        Assert.Equal(("+A2-XD2", 5, 3), (strip.GetProperty("name").GetString(), strip.GetProperty("terminals").GetInt32(), strip.GetProperty("bridges").GetInt32()));
        Assert.Equal("Listed 1 terminal strip.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task TerminalStripList_UnknownProject_IsABusinessError()
    {
        CallToolResult result = await TerminalTools.TerminalStripListAsync(_client, "Line9", null, TerminalTools.DefaultLimit, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.ProjectNotOpen, ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TerminalStripGet_ByFullName_ReturnsTheTerminalsWithTheirEndsAndBridges()
    {
        CallToolResult result = await Get("+A2-XD2");

        JsonElement data = Structured(result);
        Assert.Equal(5, data.GetProperty("total").GetInt32());
        JsonElement first = data.GetProperty("terminals")[0];
        Assert.Equal(("+A2-XD2:1", "1"), (first.GetProperty("name").GetString(), first.GetProperty("designation").GetString()));
        Assert.Equal(["=EA+A2-QA1"], first.GetProperty("inside").EnumerateArray().Select(e => e.GetProperty("device").GetString()));
        Assert.Equal(("+B1-WG2", "1"), (first.GetProperty("outside")[0].GetProperty("device").GetString(), first.GetProperty("outside")[0].GetProperty("pin").GetString()));
        Assert.Equal(["+A2-XD2:2"], first.GetProperty("bridgedWith").EnumerateArray().Select(b => b.GetString()));
        Assert.Equal("Terminal strip found: 5 terminals, 3 bridges.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task TerminalStripGet_ShortNameAndPaging_FindTheStripAndContinueItsTerminals()
    {
        JsonElement first = Structured(await Get("-xz1", limit: 30));
        JsonElement second = Structured(await Get("-xz1", limit: 30, cursor: first.GetProperty("nextCursor").GetString()));

        Assert.Equal("+B2.X1-XZ1", first.GetProperty("strip").GetProperty("name").GetString());
        Assert.Equal(["+B2.X1-XZ1:31", "+B2.X1-XZ1:32", "+B2.X1-XZ1:33"], second.GetProperty("terminals").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task TerminalStripGet_AmbiguousAndUnknownNames_AreBusinessErrors()
    {
        CallToolResult ambiguous = await Get("-XD2");
        CallToolResult unknown = await Get("-XD9");

        Assert.Contains(ErrorReason.AmbiguousName, ((TextContentBlock)ambiguous.Content[0]).Text, StringComparison.Ordinal);
        Assert.Contains("+A1-XD2", ((TextContentBlock)ambiguous.Content[0]).Text, StringComparison.Ordinal);
        Assert.Contains(ErrorReason.DeviceNotFound, ((TextContentBlock)unknown.Content[0]).Text, StringComparison.Ordinal);
    }

    private Task<CallToolResult> Get(string strip, int limit = TerminalTools.DefaultGetLimit, string? cursor = null) =>
        TerminalTools.TerminalStripGetAsync(_client, strip, null, limit, cursor, TestContext.Current.CancellationToken);

    private Task<CallToolResult> List(string? nameContains = null, int limit = TerminalTools.DefaultLimit, string? cursor = null) =>
        TerminalTools.TerminalStripListAsync(_client, null, nameContains, limit, cursor, TestContext.Current.CancellationToken);

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true }];
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

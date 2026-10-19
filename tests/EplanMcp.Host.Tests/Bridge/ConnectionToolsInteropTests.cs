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
/// eplan_connection_list through the real BridgeClient, pipe and Bridge.Core operation; only the EPLAN adapters are
/// fake (<see cref="SampleDevices"/>, <see cref="SampleConnections"/>).
/// </summary>
[Trait("Size", "Medium")]
public sealed class ConnectionToolsInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public ConnectionToolsInteropTests()
    {
        var catalog = new Catalog();
        var router = new OperationRouter(
            [new ProjectListOperation(catalog), new ConnectionListOperation(catalog, new SampleDevices(), new SampleConnections())],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000c1",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "connections", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
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
    public async Task ConnectionList_ByFullTag_ListsTheConnectionsOfTheDeviceOnEveryPage()
    {
        CallToolResult result = await List(device: "=GAA+A1-FC1");

        JsonElement data = Structured(result);
        Assert.Equal("Line1", data.GetProperty("project").GetString());
        Assert.Equal([("2", "U1"), ("4", "V1"), ("13", "1")], Ends(data).Select(e => (e.FromPin, e.ToPin)));
        JsonElement first = data.GetProperty("connections")[0];
        Assert.Equal(("1,5", "GY", "2d"), (first.GetProperty("crossSection").GetString(), first.GetProperty("colour").GetString(), first.GetProperty("kind").GetString()));
        Assert.Equal("Listed 3 connections.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task ConnectionList_DeviceAndPage_KeepsTheDevicesConnectionsOnThatPage()
    {
        JsonElement data = Structured(await List(device: "=GAA+A1-FC1", page: "=GAA+A1&EFS1/2"));

        Assert.Equal(("=GAA+A1-FC1", "=GAA+A1-MA2"), Assert.Single(Ends(data).Select(e => (e.From, e.To))));
    }

    [Fact]
    public async Task ConnectionList_ShownTagOfOneDevice_FindsItAndA3DConnectionHasNoEnds()
    {
        JsonElement data = Structured(await List(device: "-ma2"));

        Assert.Equal(3, data.GetProperty("total").GetInt32());
        JsonElement threeD = data.GetProperty("connections")[2];
        Assert.Equal("3d", threeD.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, threeD.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.Null, threeD.GetProperty("page").ValueKind);
        Assert.Equal("BU", threeD.GetProperty("colour").GetString());
    }

    [Fact]
    public async Task ConnectionList_ByPage_PagesThroughTheConnectionsAndAnUnnamedEndIsNull()
    {
        var pins = new List<string?>();
        string cursor = string.Empty;
        do
        {
            JsonElement data = Structured(await List(page: "=GAA+A1&EFS1/1", limit: 1, cursor: cursor));
            pins.AddRange(Ends(data).Select(e => e.FromPin));
            cursor = data.GetProperty("nextCursor").GetString() ?? string.Empty;
        }
        while (cursor.Length > 0);

        JsonElement other = Structured(await List(page: "=GAB1+A2&EFS1/1")).GetProperty("connections")[0];

        Assert.Equal(["2", "4"], pins);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("to").ValueKind);
    }

    [Fact]
    public async Task ConnectionList_AmbiguousUnknownAndMissingNames_AreBusinessErrors()
    {
        CallToolResult ambiguous = await List(device: "-FC1");
        CallToolResult device = await List(device: "-K99");
        CallToolResult page = await List(page: "=GAA+A1&EFS1/99");

        Assert.Equal(ErrorReason.AmbiguousName, Error(ambiguous).GetProperty("reason").GetString());
        Assert.Equal(["=GAA+A1-FC1", "=GAB1+A1-FC1"], Error(ambiguous).GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n'));
        Assert.Equal(ErrorReason.DeviceNotFound, Error(device).GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.PageNotFound, Error(page).GetProperty("reason").GetString());
    }

    private Task<CallToolResult> List(string device = "", string page = "", int limit = ConnectionTools.DefaultLimit, string cursor = "") =>
        ConnectionTools.ConnectionListAsync(_client, string.Empty, device, page, limit, cursor, TestContext.Current.CancellationToken);

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
    }

    private static (string? From, string? FromPin, string? To, string? ToPin)[] Ends(JsonElement data) =>
    [
        .. data.GetProperty("connections").EnumerateArray().Select(c => (Name(c, "from", "device"), Name(c, "from", "pin"), Name(c, "to", "device"), Name(c, "to", "pin"))),
    ];

    private static string? Name(JsonElement connection, string end, string name) =>
        connection.GetProperty(end) is { ValueKind: JsonValueKind.Object } value ? value.GetProperty(name).GetString() : null;

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

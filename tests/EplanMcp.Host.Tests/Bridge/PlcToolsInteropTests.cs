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
/// eplan_plc_get through the real BridgeClient, pipe and Bridge.Core operation; only the EPLAN adapters are fake
/// (<see cref="SampleDevices"/> with a PLC box added, and a PLC reader shaped like S21's =K1+A2-KE1 with 34 channels).
/// </summary>
[Trait("Size", "Medium")]
public sealed class PlcToolsInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public PlcToolsInteropTests()
    {
        var devices = new SampleDevices();
        devices.Functions.Add(new FunctionRecord { Name = "=K1+A2-KE1", VisibleName = "-KE1", IsMain = true, Category = "PLCBox" });
        var catalog = new Catalog();
        var router = new OperationRouter(
            [new ProjectListOperation(catalog), new PlcGetOperation(catalog, devices, new SamplePlcs())],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000c4",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "plcs", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
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
    public async Task PlcGet_ShownTag_ReturnsTheBoxAndCountsTheConnectedChannelsOfAllPages()
    {
        CallToolResult result = await Get("-KE1", limit: 10);

        JsonElement data = Structured(result);
        Assert.Equal(("=K1+A2-KE1", "-KE1"), (data.GetProperty("plc").GetProperty("tag").GetString(), data.GetProperty("plc").GetProperty("visibleTag").GetString()));
        Assert.Equal(34, data.GetProperty("total").GetInt32());
        JsonElement[] channels = [.. data.GetProperty("channels").EnumerateArray()];
        Assert.Equal(10, channels.Length);
        Assert.Equal(("=K1+A2-KE1:1.2", "%IX0.1", "=K1+A2&EFS1/3", true), (channels[2].GetProperty("name").GetString(), channels[2].GetProperty("address").GetString(), channels[2].GetProperty("page").GetString(), channels[2].GetProperty("main").GetBoolean()));
        Assert.Equal(["=K1+A2-SF1"], channels[2].GetProperty("connected").EnumerateArray().Select(e => e.GetProperty("device").GetString()));
        Assert.Equal(("=K1+A2-KE1:1.2", JsonValueKind.Null, false), (channels[3].GetProperty("name").GetString(), channels[3].GetProperty("address").ValueKind, channels[3].GetProperty("main").GetBoolean()));
        Assert.Empty(channels[3].GetProperty("connected").EnumerateArray());
        Assert.Empty(channels[0].GetProperty("connected").EnumerateArray());
        Assert.Equal("PLC found: 34 channels, 9 connected, 10 listed (more: cursor).", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task PlcGet_ChannelsOfOneName_AreAllListedAndAConnectionGoesToItsChannelObject()
    {
        JsonElement[] channels = [.. Structured(await Get("=K1+A2-KE1", limit: 100)).GetProperty("channels").EnumerateArray()];

        Assert.Equal(34, channels.Length);
        Assert.Equal(17, channels.Select(c => c.GetProperty("name").GetString()).Distinct(StringComparer.Ordinal).Count());
        JsonElement[] point = [.. channels.Where(c => c.GetProperty("name").GetString() == "=K1+A2-KE1:2.3")];
        Assert.Equal(2, point.Length);
        Assert.Equal([("+A2-XD4:3", "1")], point[0].GetProperty("connected").EnumerateArray().Select(e => (e.GetProperty("device").GetString(), e.GetProperty("pin").GetString())));
        Assert.Empty(point[1].GetProperty("connected").EnumerateArray());
    }

    [Fact]
    public async Task PlcGet_UnknownAndNotAPlcBox_AreBusinessErrors()
    {
        JsonElement unknown = Error(await Get("-KE9"));
        JsonElement motor = Error(await Get("=GAA+A1-MA1"));

        Assert.Equal(ErrorReason.DeviceNotFound, unknown.GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.InvalidArgument, motor.GetProperty("reason").GetString());
        Assert.Equal("Motor", motor.GetProperty("metadata").GetProperty("category").GetString());
    }

    private Task<CallToolResult> Get(string plc, int limit = PlcTools.DefaultLimit, string? cursor = null) =>
        PlcTools.PlcGetAsync(_client, plc, null, null, limit, cursor, TestContext.Current.CancellationToken);

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
    }

    /// <summary>
    /// =K1+A2-KE1 shaped like the fixture (Large tests): 34 channel objects with 17 names, each name a main object with
    /// an address on page 3 and an auxiliary one without on page 1. Every second name is wired on page 3 by name only,
    /// :2.3 by its object, plus an unnamed end and the box's own supply, which no channel gets; any other device is a motor.
    /// </summary>
    private sealed class SamplePlcs : IPlcReader
    {
        private const string Schematic = "=K1+A2&EFS1/3";

        public PlcRecord? ReadPlc(string projectPath, string tag, string? language) => tag switch
        {
            "=K1+A2-KE1" => new PlcRecord
            {
                Category = "PLCBox",
                Plc = new PlcInfo { Tag = tag, VisibleTag = "-KE1", Parts = ["PXC.2703994"] },
                Channels =
                [
                    .. Enumerable.Range(0, 17).SelectMany(i => new[]
                    {
                        new PlcChannel { Name = Point(tag, i), Page = Schematic, Main = true, Address = $"%IX{i / 8}.{i % 8}" },
                        new PlcChannel { Name = Point(tag, i), Page = "=K1+A2&EFS1/1" },
                    }),
                ],
                Connections =
                [
                    .. Enumerable.Range(0, 17).Where(i => i % 2 == 1).Select(i => Wire(Point(tag, i), "=K1+A2-SF" + i, "13")),
                    Wire(Point(tag, 10), "+A2-XD4:3", "1", channel: 20),
                    Wire(Point(tag, 0), string.Empty, string.Empty),
                    Wire(tag, "=K1+A2-GB1", "1"),
                ],
            },
            "=GAA+A1-MA1" => new PlcRecord { Category = "Motor" },
            _ => null,
        };

        /// <summary>:1.1 to :1.8, :2.1 to :2.8, then :3.1.</summary>
        private static string Point(string tag, int i) => $"{tag}:{(i / 8) + 1}.{(i % 8) + 1}";

        private static PlcConnectionRecord Wire(string function, string device, string pin, int? channel = null) =>
            new() { Channel = channel, Function = function, Page = Schematic, Other = new ConnectionEnd { Device = device, Pin = pin } };
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

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
        Assert.Equal(10, data.GetProperty("channels").GetArrayLength());
        JsonElement first = data.GetProperty("channels")[0];
        Assert.Equal(("=K1+A2-KE1:1", "I0.0"), (first.GetProperty("name").GetString(), first.GetProperty("address").GetString()));
        Assert.Equal(["=K1+A2-SF1"], first.GetProperty("connected").EnumerateArray().Select(e => e.GetProperty("device").GetString()));
        Assert.Empty(data.GetProperty("channels")[1].GetProperty("connected").EnumerateArray());
        Assert.Equal("PLC found: 34 channels, 17 connected, 10 listed (more: cursor).", ((TextContentBlock)result.Content[0]).Text);
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

    /// <summary>=K1+A2-KE1 with 34 channels (S21), every odd one wired to a switch, plus an unnamed end the Bridge drops; any other device is a motor.</summary>
    private sealed class SamplePlcs : IPlcReader
    {
        public PlcRecord? ReadPlc(string projectPath, string tag, string? language) => tag switch
        {
            "=K1+A2-KE1" => new PlcRecord
            {
                Category = "PLCBox",
                Plc = new PlcInfo { Tag = tag, VisibleTag = "-KE1", Parts = ["PXC.2703994"] },
                Channels =
                [
                    .. Enumerable.Range(1, 34).Select(i => new PlcChannel
                    {
                        Name = $"{tag}:{i}",
                        Address = $"I{(i - 1) / 8}.{(i - 1) % 8}",
                        Connected = i % 2 == 1
                            ? [new ConnectionEnd { Device = "=K1+A2-SF" + i, Pin = "13" }]
                            : [new ConnectionEnd { Device = string.Empty, Pin = string.Empty }],
                    }),
                ],
            },
            "=GAA+A1-MA1" => new PlcRecord { Category = "Motor" },
            _ => null,
        };
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

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
/// eplan_cable_get through the real BridgeClient, pipe and Bridge.Core operation; only the EPLAN adapters are fake
/// (<see cref="SampleDevices"/> with two cables added, and a cable reader shaped like S21's +B1-WG2).
/// </summary>
[Trait("Size", "Medium")]
public sealed class CableToolsInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public CableToolsInteropTests()
    {
        var devices = new SampleDevices();
        devices.Functions.Add(new FunctionRecord { Name = "+B1-WG2", VisibleName = "-WG2", IsMain = true, Category = "Cable" });
        devices.Functions.Add(new FunctionRecord { Name = "+A1-WG2", VisibleName = "-WG2", IsMain = true, Category = "Cable" });
        devices.Functions.Add(new FunctionRecord { Name = "+A1-WE1", VisibleName = "-WE1", IsMain = true, Category = "Cable" });
        var catalog = new Catalog();
        var router = new OperationRouter(
            [new ProjectListOperation(catalog), new CableGetOperation(catalog, devices, new SampleCables())],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000c2",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "cables", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
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
    public async Task CableGet_ByFullTag_ReturnsTheCableItsEndsAndItsCores()
    {
        CallToolResult result = await Get("+B1-WG2");

        JsonElement data = Structured(result);
        Assert.Equal("Line1", data.GetProperty("project").GetString());
        JsonElement cable = data.GetProperty("cable");
        Assert.Equal(("-WG2", "3", "1,5"), (cable.GetProperty("visibleTag").GetString(), cable.GetProperty("wireCount").GetString(), cable.GetProperty("crossSection").GetString()));
        Assert.Equal(["=F+B1-SG1"], cable.GetProperty("sources").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(["+A2-XG4:2"], cable.GetProperty("targets").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(["433", null], data.GetProperty("cores").EnumerateArray().Select(c => c.GetProperty("designation").GetString()));
        Assert.Equal("+A2-XG4", data.GetProperty("cores")[0].GetProperty("to").GetProperty("device").GetString());
        Assert.Equal("Cable found: 2 cores.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task CableGet_UniqueShownTagAndPaging_FindTheCableAndContinueItsCores()
    {
        JsonElement first = Structured(await Get("-we1", limit: 1));
        JsonElement second = Structured(await Get("-we1", limit: 1, cursor: first.GetProperty("nextCursor").GetString()));

        Assert.Equal("+A1-WE1", first.GetProperty("cable").GetProperty("tag").GetString());
        Assert.Equal(["1", "2"], new[] { first, second }.Select(d => d.GetProperty("cores")[0].GetProperty("from").GetProperty("pin").GetString()));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task CableGet_AmbiguousUnknownAndNotACable_AreBusinessErrors()
    {
        JsonElement ambiguous = Error(await Get("-WG2"));
        JsonElement unknown = Error(await Get("-WG9"));
        JsonElement motor = Error(await Get("=GAA+A1-MA1"));

        Assert.Equal(ErrorReason.AmbiguousName, ambiguous.GetProperty("reason").GetString());
        Assert.Equal(["+A1-WG2", "+B1-WG2"], ambiguous.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n').Order(StringComparer.Ordinal));
        Assert.Equal(ErrorReason.DeviceNotFound, unknown.GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.InvalidArgument, motor.GetProperty("reason").GetString());
        Assert.Equal("Motor", motor.GetProperty("metadata").GetProperty("category").GetString());
    }

    private Task<CallToolResult> Get(string cable, int limit = CableTools.DefaultLimit, string? cursor = null) =>
        CableTools.CableGetAsync(_client, cable, null, null, limit, cursor, TestContext.Current.CancellationToken);

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

    /// <summary>The cables of S21's sample (+B1-WG2: two cores, =F+B1-SG1 to the terminal +A2-XG4:2); any other device is a motor.</summary>
    private sealed class SampleCables : ICableReader
    {
        public CableRecord? ReadCable(string projectPath, string tag, string? language) => tag switch
        {
            "+B1-WG2" or "+A1-WG2" or "+A1-WE1" => new CableRecord
            {
                Category = "Cable",
                Cable = new CableInfo { Tag = tag, VisibleTag = tag[3..], WireCount = "3", CrossSection = "1,5", Sources = ["=F+B1-SG1", "=F+B1-SG1"], Targets = ["+A2-XG4:2"] },
                Cores =
                [
                    new ConnectionRecord { StartDevice = "=F+B1-SG1", StartPin = "1", EndDevice = "+A2-XG4", EndPin = "2", Colour = "BK", Designation = "433" },
                    new ConnectionRecord { StartDevice = "=F+B1-SG1", StartPin = "2", EndDevice = "+A2-XG4", EndPin = "3", Colour = "BU" },
                ],
            },
            "=GAA+A1-MA1" => new CableRecord { Category = "Motor" },
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

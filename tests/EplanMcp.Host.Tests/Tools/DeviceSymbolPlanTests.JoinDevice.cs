using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    [Fact]
    public async Task JoinDevice_ForwardsBothWriteStagesAndReportsAuxiliaryReadback()
    {
        _store.Names.Add("=F+A1-K2");
        DevicePlaceArgs? executed = null;
        _harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = value =>
        {
            executed = (DevicePlaceArgs)value!;
            return new DevicePlaceExecuteOperation(_store, _store, _store, _store, _store, null!, _store,
                (_, _, _, _) => new JoinedPlacement()).Execute(executed)!;
        };

        JsonElement result = WriteToolHarness.Structured(await DeviceWriteTools.DevicePlaceAsync(
            _harness.Pipeline, _harness.Bridge, "Line1", Page, 120, 160, "-K2", dry_run: false,
            library: "IEC_symbol", symbol: "K", symbol_variant: 0, join_device: true,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(executed!.JoinDevice);
        Assert.Equal("=F+A1-K2", result.GetProperty("device").GetProperty("tag").GetString());
        Assert.False(result.GetProperty("device").GetProperty("isMainFunction").GetBoolean());
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Single(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task JoinDevice_McpBooleanRoutesToBridgeAndExplicitFalsePartRejectsBeforeBridge()
    {
        _store.Names.Add("=F+A1-K2");
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_harness.Locations);
        builder.Services.AddSingleton(_harness.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_harness.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: TestContext.Current.CancellationToken);
            var args = new Dictionary<string, object?>
            {
                ["project"] = "Line1",
                ["page"] = Page,
                ["x"] = 120,
                ["y"] = 160,
                ["tag"] = "-K2",
                ["library"] = "IEC_symbol",
                ["symbol"] = "K",
                ["symbol_variant"] = 0,
                ["join_device"] = true
            };
            JsonElement preview = WriteToolHarness.Structured(await client.CallToolAsync(DeviceWriteTools.PlaceTool, args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("=F+A1-K2", preview.GetProperty("device").GetProperty("tag").GetString());
            int reads = _harness.Bridge.Invocations;
            args.Remove("library");
            args.Remove("symbol");
            args.Remove("symbol_variant");
            args["part"] = "SIE.3RH2122-1HB40";
            args["join_device"] = false;
            JsonElement error = WriteToolHarness.Error(await client.CallToolAsync(DeviceWriteTools.PlaceTool, args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            Assert.Contains("library", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(reads, _harness.Bridge.Invocations);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private sealed class JoinedPlacement : IDeviceSymbolPlacement
    {
        public ITransactionScope Begin(string undoDescription) => new JoinedScope();
        public DevicePlaceData Place(DevicePlaceArgs args, string tag) => new() { Page = args.Page, Tag = tag, IsMainFunction = false, Functions = 1 };
        public IReadOnlyList<DeviceSymbolDisplay> ReadDisplays() => [];
        private sealed class JoinedScope : ITransactionScope
        {
            public void Commit() { }
            public void CloseUndo() { }
            public void Rollback() { }
            public void Dispose() { }
        }
    }
}

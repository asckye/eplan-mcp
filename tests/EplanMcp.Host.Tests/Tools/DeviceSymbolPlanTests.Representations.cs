using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("I0.0")]
    public async Task Plc_SameChannelRepresentationPreservesAddressAndOtherChannelsStillConflict(string? address)
    {
        PlcSymbol();
        _store.Names.Add("+A1-KF1:1.1");
        _store.Channels.Add(new() { Name = "+A1-KF1:1.1", Page = "+A1&EFA1/4", Address = address, Main = true });
        var result = WriteToolHarness.Structured(await DeviceWriteTools.DevicePlaceAsync(
            _harness.Pipeline, _harness.Bridge, "Line1", Page, 104, 92, "-KF1", dry_run: false,
            library: "IEC_symbol", symbol: "PLC_DI", symbol_variant: 0, plc_connection_point: "1.1", plc_address: address,
            plc_text: "Voltage US", plc_plug_designation: "1", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(2, _store.Channels.Count(c => c.Name == "+A1-KF1:1.1"));
        Assert.False(_store.Channels[^1].Main);
        if (address is not null)
        {
            var error = WriteToolHarness.Error(await Plc(point: "another", address: address));
            Assert.Equal(ErrorReason.AlreadyExists, error.GetProperty("reason").GetString());
        }
        Assert.Equal("CloseUndo", _placement.Calls[^3]);
    }
}

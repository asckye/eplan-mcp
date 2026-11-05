using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartTerminal_ExactInputPreviewsTemplateOrOverride(bool replace)
    {
        _store.Parts.Parts.Add(new() { PartNumber = "PXC.3212140", Variant = "1" });
        _store.Symbols.Add(new() { Library = "IEC_symbol", Name = "XTR1", Variant = 1, Type = "Function", Kind = "function", FunctionCategory = "Terminal" });
        JsonElement result = WriteToolHarness.Structured(await DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, _harness.Bridge,
            "Line1", Page, 80, 160, part: "PXC.3212140", terminal_strip: "+A1-XQ10", terminal_number: "1",
            library: replace ? "IEC_symbol" : null, symbol: replace ? "XTR1" : null, symbol_variant: replace ? 1 : null,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
        Assert.Equal("PXC.3212140", result.GetProperty("part").GetString());
        Assert.Equal("+A1-XQ10:1", result.GetProperty("device").GetProperty("tag").GetString());
        Assert.Equal("1", result.GetProperty("terminal").GetProperty("number").GetString());
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(_placement.Calls);
    }

    [Fact]
    public async Task PartOverride_MissingSymbolRefusesBeforeWrite()
    {
        _store.Parts.Parts.Add(new() { PartNumber = "SIE.5SY4106-7", Variant = "1" });
        JsonElement error = WriteToolHarness.Error(await DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, _harness.Bridge,
            "Line1", Page, 80, 160, tag: "-F1", part: "SIE.5SY4106-7", library: "IEC_symbol", symbol: "MISSING", symbol_variant: 0,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.SymbolNotFound, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(_placement.Calls);
    }
}

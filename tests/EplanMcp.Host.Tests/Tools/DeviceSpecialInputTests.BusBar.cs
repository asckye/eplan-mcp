using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSpecialInputTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BusBarDefinition_RequiresTagBeforeAnyPipelineCall(string? tag)
    {
        var result = await DeviceWriteTools.DevicePlaceAsync(null!, null!, "Line1", "=GAA+A1&EFS1/60", 80, 140,
            tag: tag, library: "SPECIAL", symbol: "BBDEF", symbol_variant: 0,
            cancellationToken: TestContext.Current.CancellationToken);
        var error = WriteToolHarness.Error(result);
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted),
            (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Fact]
    public void BusBarDefinition_AcceptsTaggedSymbolSource()
    {
        var args = new DevicePlaceArgs { Project = "Line1", Library = "SPECIAL", Symbol = "BBDEF", SymbolVariant = 0, Tag = "-WE9" };
        Assert.Empty(DevicePlaceInputs.Check(args, null));
    }
}

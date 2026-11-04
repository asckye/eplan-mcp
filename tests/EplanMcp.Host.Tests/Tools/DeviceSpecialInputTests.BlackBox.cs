using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed partial class DeviceSpecialInputTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 180d)]
    [InlineData(140d, null)]
    [InlineData(80d, 180d)]
    [InlineData(140d, 140d)]
    [InlineData(double.NaN, 180d)]
    public void BlackBox_RequiresFiniteOppositeCorners(double? x, double? y)
    {
        DevicePlaceArgs args = BlackBox();
        args.CornerX = x;
        args.CornerY = y;
        Assert.Contains(DevicePlaceInputs.Check(args, null), p => p.StartsWith("/corner_x:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlackBox_MissingTagFailsBeforeAnyPipelineCall(string? tag)
    {
        var result = await DeviceWriteTools.DevicePlaceAsync(null!, null!, "Line1", "=GAA+A1&EFS1/60", 80, 140,
            tag: tag, library: "SPECIAL", symbol: "DC", symbol_variant: 0, corner_x: 140, corner_y: 180,
            cancellationToken: TestContext.Current.CancellationToken);
        var error = WriteToolHarness.Error(result);
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted),
            (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Fact]
    public void BlackBox_UsesSymbolSourceAndTwoCorners()
    {
        Assert.Empty(DevicePlaceInputs.Check(BlackBox(), null));
    }

    private static DevicePlaceArgs BlackBox() => new()
    {
        Project = "Line1",
        Page = "=GAA+A1&EFS1/60",
        Library = "SPECIAL",
        Symbol = "DC",
        SymbolVariant = 0,
        Tag = "-PF9",
        X = 80,
        Y = 140,
        CornerX = 140,
        CornerY = 180
    };
}

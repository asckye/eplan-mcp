using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DevicePlaceOperationTests
{
    [Fact]
    public void Arrangement_ChangesFingerprint_AndReadsBackPartPlacement()
    {
        var args = Args(NewPage, "-Q20");
        string baseline = Plan(args).Fingerprint;
        args.PropertyArrangement = "左,0°";
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.NotEqual(baseline, args.Fingerprint);
        _placer.Result = tag => new() { MainName = tag, Names = [tag], OnPage = 1, PropertyArrangement = "左,0°", ActivePropertySet = "左,0°" };
        DevicePlaceData result = Execute(args);
        Assert.Equal(("左,0°", "左,0°"), (result.PropertyArrangement, result.ActivePropertySet));
        Assert.Single(_transactions.Calls, call => call == "Commit");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("左\n0°")]
    public void Arrangement_InvalidNameRefusesBeforeWriting(string arrangement)
    {
        var args = Args(NewPage, "-Q20");
        args.PropertyArrangement = arrangement;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Empty(_placer.Places);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Arrangement_MissingProperty19307WarnsWithoutRefusing()
    {
        var args = Args(NewPage, "-Q20");
        args.PropertyArrangement = "左,0°";
        args.Fingerprint = Plan(args).Fingerprint;
        _placer.Result = tag => new() { MainName = tag, Names = [tag], OnPage = 1, PropertyArrangement = "左,0°" };
        Assert.Contains("19307", Execute(args).Warning, StringComparison.Ordinal);
    }
}

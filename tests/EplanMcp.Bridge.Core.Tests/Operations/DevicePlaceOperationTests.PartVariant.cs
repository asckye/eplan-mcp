using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DevicePlaceOperationTests
{
    [Fact]
    public void PartVariantOnly_UsesTemplateAndBindsVariantToPlan()
    {
        _parts.Content.Parts.Add(new() { PartNumber = "PXC.3211813", Variant = "1" });
        var args = Args(NewPage, "", "PXC.3211813");
        string baseline = Plan(args).Fingerprint;
        args.SymbolVariant = 1;
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.NotEqual(baseline, args.Fingerprint);
        _placer.Result = tag => new() { MainName = tag, Names = [tag], OnPage = 1, SymbolVariant = 1 };
        Assert.Equal(1, Execute(args).SymbolVariant);
        Assert.Equal("PXC.3211813", Assert.Single(_placer.Places).Part);
        Assert.Null(args.Library);
        Assert.Null(args.Symbol);
    }

    [Fact]
    public void PartVariantOnly_NegativeNumberRefusesBeforeWrite()
    {
        var args = Args(NewPage, "-Q20");
        args.SymbolVariant = -1;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_placer.Places);
    }
}

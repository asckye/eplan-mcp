using EplanMcp.Contracts;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>The merger's reasons reach clients unchanged, so they must equal the contract's codes.</summary>
[Trait("Size", "Small")]
public sealed class ConfigMergerReasonTests
{
    [Fact]
    public void Reasons_EqualTheContractCodes()
    {
        Assert.Equal(ErrorReason.PackInvalid, ConfigMerger.PackInvalid);
        Assert.Equal(ErrorReason.ConfigConflict, ConfigMerger.ConfigConflict);
        Assert.Equal(ErrorReason.InvalidArgument, ConfigMerger.InvalidArgument);
    }
}

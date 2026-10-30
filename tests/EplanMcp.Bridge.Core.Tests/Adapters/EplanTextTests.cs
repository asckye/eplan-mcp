using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class EplanTextTests
{
    [Fact]
    public void Language_IndependentBypassesIsoValidityAndReadsFunctionText()
    {
        using var iso = new ISOCode("??_??");
        Assert.False(iso.IsValid());
        TextLanguage language = EplanText.Language("??_??");
        Assert.Equal(ISOCode.Language.L___, language.Number);
        Assert.False(language.Fallback);
        using var property = new PropertyValue("??_??@Start input;");
        Assert.Equal(("Start input", (string?)null), EplanText.ReadWithLanguage(property, language));
    }

    [Fact]
    public void Language_RealUnknownAndOmittedCodes_KeepTheirRules()
    {
        Assert.Equal(ISOCode.Language.en_US, EplanText.Language("en_US").Number);
        Assert.False(EplanText.Language("en_US").Fallback);
        Assert.True(EplanText.Language(null).Fallback);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => EplanText.Language("xx_YY")).Error.Reason);
    }
}

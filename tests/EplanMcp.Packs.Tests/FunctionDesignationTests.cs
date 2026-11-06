namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class FunctionDesignationTests
{
    [Theory]
    [InlineData("=GAA+A1-K1", "K")]
    [InlineData("=GAA+A1-2K1", "K")]
    [InlineData("=GAA+A1-2K1:13", "K")]
    [InlineData("+A1-XQ10:1", "XQ")]
    [InlineData("-XQ10", "XQ")]
    [InlineData("-S1", "S")]
    [InlineData("=GAA-A1-2K1:13", "K")]
    [InlineData("-k1", "k")]
    [InlineData("K1", null)]
    [InlineData("-123", null)]
    [InlineData("-123:K", null)]
    [InlineData("-:1", null)]
    [InlineData("-", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Letters_ExtractsInitialLettersAfterLastDashAndDigits(string? name, string? expected) =>
        Assert.Equal(expected, FunctionDesignation.Letters(name));
}

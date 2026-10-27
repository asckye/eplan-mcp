using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class DrawingLocatorsTests
{
    [Fact]
    public void Parse_SessionPrefixChanges_IdentityRemainsEqual()
    {
        DrawingLocator first = DrawingLocators.FromIdentifier(@"D:\Scratch\Panel", "+A1/1", "53/4/42/0", new string('a', 64));
        DrawingLocator reopened = DrawingLocators.FromIdentifier(@"d:/scratch/panel.elk", "+A1/1", "84/4/42/0", new string('a', 64));

        Assert.Equal((4, 42L), (first.Type, first.Oid));
        Assert.True(DrawingLocators.Matches(first, reopened));
    }

    [Theory]
    [InlineData("53/4/42/0", "b")]
    [InlineData("53/5/42/0", "a")]
    [InlineData("53/4/43/0", "a")]
    public void Match_ReusedNumberOrChangedGeometry_IsStale(string identifier, string digest)
    {
        DrawingLocator expected = DrawingLocators.FromIdentifier("project", "+A1/1", "53/4/42/0", new string('a', 64));
        DrawingLocator actual = DrawingLocators.FromIdentifier("project", "+A1/1", identifier, new string(digest[0], 64));
        Assert.False(DrawingLocators.Matches(expected, actual));
        actual = DrawingLocators.FromIdentifier("other", "+A1/1", "53/4/42/0", new string('a', 64));
        Assert.False(DrawingLocators.Matches(expected, actual));
        actual = DrawingLocators.FromIdentifier("project", "+A2/1", "53/4/42/0", new string('a', 64));
        Assert.False(DrawingLocators.Matches(expected, actual));
    }

    [Theory]
    [InlineData("53/4/42")]
    [InlineData("handle")]
    [InlineData("53/4/-1/0")]
    [InlineData("53/4/42/1")]
    [InlineData("53/0/42/0")]
    [InlineData("53/4/0/0")]
    [InlineData("53/2147483648/42/0")]
    [InlineData("53/4/9223372036854775808/0")]
    public void Parse_MalformedIdentifier_Rejects(string identifier) =>
        Assert.Throws<BridgeErrorException>(() => DrawingLocators.FromIdentifier("project", "+A1/1", identifier, new string('a', 64)));

    [Theory]
    [InlineData("", "page", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("project", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("project", "page", "abc")]
    [InlineData("project", "page", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Parse_IncompleteIdentity_RejectsWithPointer(string project, string page, string digest) =>
        Assert.Contains("/locator", Assert.Throws<BridgeErrorException>(() => DrawingLocators.FromIdentifier(project, page, "53/4/42/0", digest)).Error.Metadata!["violations"], StringComparison.Ordinal);
}

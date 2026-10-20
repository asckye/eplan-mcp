using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class FingerprintTests
{
    [Fact]
    public void Of_IgnoresTheOrderOfTheLines()
    {
        Assert.Equal(Fingerprint.Of(["=A/1", "=A/2", "电源"]), Fingerprint.Of(["电源", "=A/2", "=A/1"]));
    }

    [Fact]
    public void Of_ChangesWithAnyLine()
    {
        string before = Fingerprint.Of(["=A/1", "=A/2"]);

        Assert.NotEqual(before, Fingerprint.Of(["=A/1", "=A/2", "=A/3"]));
        Assert.NotEqual(before, Fingerprint.Of(["=A/1", "=a/2"]));
        Assert.NotEqual(before, Fingerprint.Of(["=A/1=A/2"]));
    }

    [Fact]
    public void Of_IsLowercaseHexSha256()
    {
        // SHA-256 of the empty string.
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Fingerprint.Of([]));
    }
}

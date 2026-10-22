namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class VersionTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.0.1-alpha.1+build.5")]
    [InlineData("10.20.30-rc.1")]
    public void SemanticVersion_Valid_Parses(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion? version));
        Assert.Equal(text, version.ToString());
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-01")]
    [InlineData("99999999999.0.0")]
    [InlineData("1.0.0 ")]
    [InlineData("١.0.0")]
    [InlineData(null)]
    public void SemanticVersion_Invalid_DoesNotParse(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void SemanticVersion_Precedence_FollowsSemVer()
    {
        // The example of semver.org section 11.
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0"];

        for (int i = 1; i < ordered.Length; i++)
        {
            Assert.True(SemanticVersion.Parse(ordered[i - 1]) < SemanticVersion.Parse(ordered[i]), $"{ordered[i - 1]} < {ordered[i]}");
        }

        Assert.Equal(SemanticVersion.Parse("1.0.0+a"), SemanticVersion.Parse("1.0.0+b"));
    }

    [Theory]
    [InlineData("^1.2.0", "1.2.0", true)]
    [InlineData("^1.2.0", "1.9.9", true)]
    [InlineData("^1.2.0", "2.0.0", false)]
    [InlineData("^1.2.0", "1.1.9", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("~1.2.0", "1.2.7", true)]
    [InlineData("~1.2.0", "1.3.0", false)]
    [InlineData(">=1.2.0 <2.0.0", "1.5.0", true)]
    [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("=1.2.3", "1.2.4", false)]
    [InlineData("*", "7.0.0", true)]
    [InlineData("*", "7.0.0-beta", false)]
    [InlineData("^1.2.0", "1.3.0-beta", false)]
    [InlineData("^1.3.0-beta", "1.3.0-rc.1", true)]
    [InlineData("^1.3.0-beta", "2.0.0-beta", false)]
    public void VersionRange_Contains(string range, string version, bool expected)
    {
        Assert.True(VersionRange.TryParse(range, out VersionRange? parsed));
        Assert.Equal(expected, parsed.Contains(SemanticVersion.Parse(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.x")]
    [InlineData(">= 1.0.0")]
    [InlineData("^1")]
    [InlineData("* 1.0.0")]
    [InlineData("1.0.0 || 2.0.0")]
    public void VersionRange_Invalid_DoesNotParse(string range)
    {
        Assert.False(VersionRange.TryParse(range, out _));
    }

    [Theory]
    [InlineData("2026.0", "2026.0.3.25421", true)]
    [InlineData("2026.0", "2026.1", false)]
    [InlineData("2026", "2026.0.3", true)]
    [InlineData(">=2026.0 <2027", "2026.0.3.25421", true)]
    [InlineData(">=2026.0 <2027", "2027.0", false)]
    [InlineData(">=2025", "2.7.11561", false)]
    [InlineData("*", "2.7", true)]
    [InlineData("=2026.0.3", "2026.0.3.25421", true)]
    public void EplanVersionRange_Contains(string range, string version, bool expected)
    {
        Assert.True(EplanVersionRange.TryParse(range, out EplanVersionRange? parsed));
        Assert.Equal(expected, parsed.Contains(version));
    }

    [Theory]
    [InlineData("2026.x")]
    [InlineData("^2026")]
    [InlineData("2026.0.3.1.2")]
    [InlineData("")]
    public void EplanVersionRange_Invalid_DoesNotParse(string range)
    {
        Assert.False(EplanVersionRange.TryParse(range, out _));
    }
}

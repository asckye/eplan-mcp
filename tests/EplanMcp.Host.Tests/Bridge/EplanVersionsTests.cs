using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Bridge;

[Trait("Size", "Small")]
public sealed class EplanVersionsTests
{
    [Theory]
    [InlineData("2026.0.3.25421", "Verified")]
    [InlineData("2024.0.3.21408", "Expected")]
    [InlineData("2025.0.3", "Expected")]
    [InlineData("2027.0", "Expected")]
    [InlineData("2023.0.3.19351", "Unsupported")]
    [InlineData("2028.0", "Unsupported")]
    [InlineData("?", "Unsupported")]
    [InlineData("", "Unsupported")]
    [InlineData(null, "Unsupported")]
    public void Support_FollowsAdr0009(string? version, string expected)
    {
        Assert.Equal(expected, EplanVersions.Support(version).ToString());
    }
}

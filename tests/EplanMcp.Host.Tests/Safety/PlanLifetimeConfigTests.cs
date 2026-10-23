using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

[Trait("Size", "Small")]
public sealed class PlanLifetimeConfigTests
{
    [Theory]
    [InlineData("{}", 24)]
    [InlineData("{\"planLifetimeHours\":1}", 1)]
    [InlineData("{\"planLifetimeHours\":12}", 12)]
    [InlineData("{\"planLifetimeHours\":24}", 24)]
    public void Parse_OnlyShortensTheDefault(string json, int expected)
    {
        (HostConfig? config, string? problem) = HostConfig.Parse(json, new HostDataLocations("C:\\Data"));
        Assert.Null(problem);
        Assert.Equal(expected, config!.PlanLifetimeHours);
    }

    [Theory]
    [InlineData("{\"planLifetimeHours\":0}")]
    [InlineData("{\"planLifetimeHours\":25}")]
    [InlineData("{\"planLifetimeHours\":1.5}")]
    [InlineData("{\"planLifetimeHours\":\"1\"}")]
    [InlineData("{\"planLifetimeHours\":null}")]
    [InlineData("{\"planLifetimeHours\":true}")]
    [InlineData("{\"PlanLifetimeHours\":1}")]
    [InlineData("{\"planLifetimeHours\":1,\"planLifetimeHours\":24}")]
    public void Parse_InvalidTypeRangeCaseOrDuplicate_RefusesTheFile(string json)
    {
        (HostConfig? config, string? problem) = HostConfig.Parse(json, new HostDataLocations("C:\\Data"));
        Assert.Null(config);
        Assert.NotNull(problem);
    }
}

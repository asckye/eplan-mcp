using System.Text.Json;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class LiveFixtureProjectsTests
{
    private const string Fixture = @"C:\Scratch\fixtures\EPLAN_Sample_Project";

    [Fact]
    public void Acceptance_LargeWriteIsCurrent_UsesTheOpenReadFixtureByExactPath()
    {
        JsonElement projects = JsonSerializer.SerializeToElement(new
        {
            projects = new[] { new { path = @"C:\Scratch\writes\LargeWrite", current = true }, new { path = Fixture, current = false } }
        });
        Assert.Equal(Fixture, LiveFixtureProjects.Resolve(projects, Fixture));
    }

    [Theory]
    [InlineData(@"C:\Scratch\writes\LargeWrite")]
    [InlineData(@"C:\Other\fixtures\EPLAN_Sample_Project")]
    [InlineData(@"C:\Scratch\fixtures\EPLAN_Sample_ProjectCopy")]
    public void FixtureMissing_DoesNotSubstituteAnotherProject(string path)
    {
        JsonElement projects = JsonSerializer.SerializeToElement(new { projects = new[] { new { path, current = true } } });
        Assert.Throws<InvalidOperationException>(() => LiveFixtureProjects.Resolve(projects, Fixture));
    }
}

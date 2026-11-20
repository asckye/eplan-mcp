using EplanMcp.Host.Safety;
using Microsoft.Extensions.DependencyInjection;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>ADR-0061: a write is decided with writesOutsideScratch as config.json says at that moment; a file that cannot be used never allows.</summary>
[Trait("Size", "Medium")]
public sealed class LiveWritePolicyTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-live-policy-");

    private HostDataLocations Locations => new(_root.FullName);

    public void Dispose() => _root.Delete(recursive: true);

    private HostConfig Startup(OutsideScratchWrites value) => HostConfig.Defaults(Locations) with { WritesOutsideScratch = value };

    private HostConfig Startup(string value) => Startup(Enum.Parse<OutsideScratchWrites>(value));

    private void Write(string json) => File.WriteAllText(Locations.ConfigFile, json);

    [Theory]
    [InlineData("Deny")]
    [InlineData("Approve")]
    [InlineData("Allow")]
    public void WithoutAFile_TheStartupConfigurationStands(string startup)
    {
        HostConfig configured = Startup(startup);

        Assert.Same(configured, new LiveWritePolicy(configured, Locations).Current());
    }

    [Fact]
    public void TheValueFollowsTheFile_InBothDirections_WithoutARestart()
    {
        var policy = new LiveWritePolicy(Startup(OutsideScratchWrites.Approve), Locations);

        Write("""{"writesOutsideScratch":"allow"}""");
        Assert.Equal(OutsideScratchWrites.Allow, policy.Current().WritesOutsideScratch);

        // The engineer turns approval back on: the Host that started while it was off must ask again.
        Write("""{"writesOutsideScratch":"approve"}""");
        Assert.Equal(OutsideScratchWrites.Approve, policy.Current().WritesOutsideScratch);

        Write("""{"writesOutsideScratch":"deny"}""");
        Assert.Equal(OutsideScratchWrites.Deny, policy.Current().WritesOutsideScratch);
    }

    [Fact]
    public void AHostStartedWithAllow_AsksAgainOnceTheFileSaysApprove()
    {
        var policy = new LiveWritePolicy(Startup(OutsideScratchWrites.Allow), Locations);
        Write("""{"writesOutsideScratch":"approve"}""");

        HostConfig now = policy.Current();

        Assert.Equal(PolicyVerdict.NeedsApproval, PolicyEngine.Decide(ToolTier.Write, inScratch: false, now).Verdict);
    }

    [Theory]
    [InlineData("Allow", "{ not json", "Approve")]
    [InlineData("Allow", """{"writesOutsideScratch":"sometimes"}""", "Approve")]
    [InlineData("Approve", "{ not json", "Approve")]
    [InlineData("Deny", "{ not json", "Deny")]
    public void AFileThatIsNotValid_NeverAllows(string startup, string json, string expected)
    {
        Write(json);

        Assert.Equal(expected, new LiveWritePolicy(Startup(startup), Locations).Current().WritesOutsideScratch.ToString());
    }

    [Fact]
    public void TheServerSetup_RegistersIt_SoThatThePipelineDecidesWithTheLiveValue()
    {
        // The pipeline and the plan orchestrator take it as an optional service: without the registration they would
        // silently keep the start-up value.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Locations).AddSingleton(Startup(OutsideScratchWrites.Approve));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<LiveWritePolicy>());
    }

    [Fact]
    public void OnlyThePolicyFollowsTheFile_ScratchRootsStayAsStarted()
    {
        HostConfig startup = Startup(OutsideScratchWrites.Approve);
        Write("""{"writesOutsideScratch":"allow","scratchRoots":["C:\\Elsewhere"]}""");

        HostConfig now = new LiveWritePolicy(startup, Locations).Current();

        Assert.Equal(OutsideScratchWrites.Allow, now.WritesOutsideScratch);
        Assert.Equal(startup.ScratchRoots, now.ScratchRoots);
    }
}

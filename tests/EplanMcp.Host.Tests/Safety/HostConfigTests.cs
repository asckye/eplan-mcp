using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

[Trait("Size", "Small")]
public sealed class HostConfigTests
{
    private static readonly HostDataLocations _locations = new("C:\\Data\\EplanMcp");

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public void Parse_NothingSet_GivesTheDefaults(string json)
    {
        (HostConfig? config, string? problem) = HostConfig.Parse(json, _locations);

        Assert.Null(problem);
        Assert.Equal(["C:\\Data\\EplanMcp\\scratch"], config!.ScratchRoots);
        Assert.Equal(OutsideScratchWrites.Deny, config.WritesOutsideScratch);
    }

    [Fact]
    public void Parse_ReadsBothSettings()
    {
        (HostConfig? config, _) = HostConfig.Parse("""{"scratchRoots":["D:\\Scratch","E:\\Copies"],"writesOutsideScratch":"approve"}""", _locations);

        Assert.Equal(["D:\\Scratch", "E:\\Copies"], config!.ScratchRoots);
        Assert.Equal(OutsideScratchWrites.Approve, config.WritesOutsideScratch);
    }

    [Theory]
    [InlineData("""{"scratchRoot":"D:\\Scratch"}""", "scratchRoot")]
    [InlineData("""{"writesOutsideScratch":"allow"}""", "is not valid")]
    [InlineData("""{"writesOutsideScratch":1}""", "is not valid")]
    [InlineData("""{"scratchRoots":["Scratch"]}""", "full path")]
    [InlineData("""{"scratchRoots":[]}""", "full path")]
    [InlineData("""{"scratchRoots":[" "]}""", "full path")]
    [InlineData("""{"scratchRoots":""", "is not valid")]
    public void Parse_AnInvalidFile_IsRefusedWithTheReason(string json, string reason)
    {
        (HostConfig? config, string? problem) = HostConfig.Parse(json, _locations);

        Assert.Null(config);
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }
}

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
        Assert.Equal(OutsideScratchWrites.Approve, config.WritesOutsideScratch);
        Assert.Equal(TimeSpan.FromSeconds(120), config.ApprovalTimeout);
        Assert.Equal(MasterDataWrites.Approve, config.MasterDataWrites);
        Assert.Empty(config.PackSources);
    }

    [Fact]
    public void Parse_ReadsEverySetting()
    {
        (HostConfig? config, _) = HostConfig.Parse("""{"scratchRoots":["D:\\Scratch","E:\\Copies"],"writesOutsideScratch":"deny","approvalTimeoutSeconds":90,"masterDataWrites":"deny","packSources":{"company-refs":"\\\\server\\eplan\\refs","local":"D:\\Refs"}}""", _locations);

        Assert.Equal(["D:\\Scratch", "E:\\Copies"], config!.ScratchRoots);
        Assert.Equal(OutsideScratchWrites.Deny, config.WritesOutsideScratch);
        Assert.Equal(TimeSpan.FromSeconds(90), config.ApprovalTimeout);
        Assert.Equal(MasterDataWrites.Deny, config.MasterDataWrites);
        Assert.Equal(new Dictionary<string, string> { ["company-refs"] = "\\\\server\\eplan\\refs", ["local"] = "D:\\Refs" }, config.PackSources);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(300)]
    public void Parse_TheApprovalDeadlineMayBeAnyWholeSecondFrom60To300(int seconds)
    {
        (HostConfig? config, _) = HostConfig.Parse($$"""{"approvalTimeoutSeconds":{{seconds}}}""", _locations);

        Assert.Equal(TimeSpan.FromSeconds(seconds), config!.ApprovalTimeout);
    }

    [Theory]
    [InlineData("""{"scratchRoot":"D:\\Scratch"}""", "scratchRoot")]
    [InlineData("""{"writesOutsideScratch":"allow"}""", "is not valid")]
    [InlineData("""{"writesOutsideScratch":1}""", "is not valid")]
    [InlineData("""{"masterDataWrites":"allow"}""", "is not valid")]
    [InlineData("""{"masterDataWrites":true}""", "is not valid")]
    [InlineData("""{"scratchRoots":["Scratch"]}""", "full path")]
    [InlineData("""{"scratchRoots":[]}""", "full path")]
    [InlineData("""{"scratchRoots":[" "]}""", "full path")]
    [InlineData("""{"scratchRoots":""", "is not valid")]
    [InlineData("""{"approvalTimeoutSeconds":59}""", "from 60 to 300")]
    [InlineData("""{"approvalTimeoutSeconds":301}""", "from 60 to 300")]
    [InlineData("""{"approvalTimeoutSeconds":-1}""", "from 60 to 300")]
    [InlineData("""{"approvalTimeoutSeconds":120.5}""", "is not valid")]
    [InlineData("""{"approvalTimeoutSeconds":"120"}""", "is not valid")]
    [InlineData("""{"packSources":{"c":"D:\\Refs"}}""", "packSources")]
    [InlineData("""{"packSources":{"Company":"D:\\Refs"}}""", "packSources")]
    [InlineData("""{"packSources":{"refs":"Refs"}}""", "packSources")]
    [InlineData("""{"packSources":{"refs":""}}""", "packSources")]
    [InlineData("""{"packSources":["D:\\Refs"]}""", "is not valid")]
    public void Parse_AnInvalidFile_IsRefusedWithTheReason(string json, string reason)
    {
        (HostConfig? config, string? problem) = HostConfig.Parse(json, _locations);

        Assert.Null(config);
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }
}

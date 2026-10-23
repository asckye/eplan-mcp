using EplanMcp.Workbench.Commands;

namespace EplanMcp.Workbench.Tests.Commands;

[Trait("Size", "Small")]
public sealed class ProgramLocationTests
{
    [Theory]
    [InlineData(@"D:\EplanMcp")]
    [InlineData(@"D:\Long folder\A completely different name")]
    [InlineData(@"D:\工程师's $tools\Portable")]
    public void ExtractedPaths_DoNotDependOnTheProgramFolderName(string folder)
    {
        string workbench = Path.Combine(folder, "workbench");
        string host = Path.Combine(folder, "eplanmcp.exe");

        Assert.Equal(host, ProgramLocation.HostBesideWorkbench(workbench));
        Assert.Equal(host, ProgramLocation.HostBesideWorkbench(workbench + "\\"));
    }

    [Theory]
    [InlineData("Release")]
    [InlineData("Debug")]
    public void RepositoryPaths_UseTheSameConfiguration(string configuration)
    {
        string source = @"D:\A repo with spaces\src";
        string host = Path.Combine(source, "EplanMcp.Host", "bin", configuration, "net10.0");
        string workbench = Path.Combine(source, "EplanMcp.Workbench", "bin", configuration, "net10.0-windows");

        Assert.Equal(Path.Combine(host, "eplanmcp.exe"), ProgramLocation.HostBesideWorkbench(workbench));
    }
}

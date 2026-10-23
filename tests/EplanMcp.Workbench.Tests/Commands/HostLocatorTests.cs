using EplanMcp.Workbench.Commands;

namespace EplanMcp.Workbench.Tests.Commands;

/// <summary>
/// Which eplanmcp.exe the Workbench runs (M5 design 4.1, 4.4): the one installed beside it, or the one built beside it in
/// the repository, and no other.
/// </summary>
[Trait("Size", "Medium")]
public sealed class HostLocatorTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-workbench-locator-");

    public void Dispose() => _root.Delete(recursive: true);

    [Theory]
    [InlineData("app")]
    [InlineData("EplanMcp-0.1.0")]
    [InlineData("Portable folder with spaces")]
    public void Installed_TheHostIsAboveTheWorkbenchWhateverTheProgramFolderIsNamed(string folder)
    {
        string workbench = Folder(folder, "workbench");
        string host = Touch(folder, "eplanmcp.exe");

        Assert.Equal(host, ProgramLocation.FindHost(workbench));
        Assert.Equal(host, ProgramLocation.FindHost(workbench + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("app", "bin")]
    public void AFolderOnlyPlacedLikeTheInstalledOne_FindsNoHost(string app, string workbench)
    {
        string folder = Folder(app, workbench);
        Touch(app, "eplanmcp.exe");

        Assert.Null(ProgramLocation.HostBesideWorkbench(folder));
        Assert.Null(ProgramLocation.FindHost(folder));
    }

    [Fact]
    public void TheInstalledFolders_AreMatchedWhateverTheirCase()
    {
        string host = Touch("App", "eplanmcp.exe");

        Assert.Equal(host, ProgramLocation.FindHost(Folder("App", "Workbench")));
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void Built_TheHostIsTheOneOfTheSameConfiguration(string configuration)
    {
        string workbench = Folder("repo", "src", "EplanMcp.Workbench", "bin", configuration, "net10.0-windows");
        Touch("repo", "src", "EplanMcp.Host", "bin", configuration == "Debug" ? "Release" : "Debug", "net10.0", "eplanmcp.exe");
        Assert.Null(ProgramLocation.FindHost(workbench));

        string host = Touch("repo", "src", "EplanMcp.Host", "bin", configuration, "net10.0", "eplanmcp.exe");

        Assert.Equal(host, ProgramLocation.FindHost(workbench));
    }

    [Fact]
    public void AnythingElse_FindsNoHost_NotEvenOneNearby()
    {
        string tests = Folder("repo", "tests", "EplanMcp.Workbench.Tests", "bin", "Debug", "net10.0-windows");
        Touch("repo", "tests", "EplanMcp.Workbench.Tests", "bin", "Debug", "net10.0-windows", "eplanmcp.exe");
        Touch("repo", "src", "EplanMcp.Host", "bin", "Debug", "net10.0", "eplanmcp.exe");
        string loose = Folder("loose");

        Assert.Null(ProgramLocation.FindHost(tests));
        Assert.Null(ProgramLocation.FindHost(loose));
        Assert.Null(ProgramLocation.HostBesideWorkbench(loose));
    }

    [Fact]
    public void ThisRepositorysWorkbench_FindsTheHostBuiltWithIt()
    {
        // The tests run from tests\EplanMcp.Workbench.Tests\bin\<configuration>\<tfm>\, and the solution builds both.
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string configuration = output.Parent!.Name;
        string repository = output.Parent.Parent!.Parent!.Parent!.Parent!.FullName;
        string workbench = Path.Combine(repository, "src", "EplanMcp.Workbench", "bin", configuration, "net10.0-windows");

        Assert.Equal(Path.Combine(repository, "src", "EplanMcp.Host", "bin", configuration, "net10.0", "eplanmcp.exe"), ProgramLocation.FindHost(workbench));
    }

    private string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([_root.FullName, .. parts])).FullName;

    private string Touch(params string[] parts)
    {
        string file = Path.Combine([_root.FullName, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, []);
        return file;
    }
}

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

    [Fact]
    public void Installed_TheHostIsInTheAppFolderAboveTheWorkbench()
    {
        string workbench = Folder("app", "workbench");
        string host = Touch("app", "eplanmcp.exe");

        Assert.Equal(host, HostLocator.Find(workbench));
        Assert.Equal(host, HostLocator.Find(workbench + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("other", "workbench")]
    [InlineData("app", "bin")]
    public void AFolderOnlyPlacedLikeTheInstalledOne_FindsNoHost(string app, string workbench)
    {
        string folder = Folder(app, workbench);
        Touch(app, "eplanmcp.exe");

        Assert.Null(HostLocator.Installed(folder));
        Assert.Null(HostLocator.Find(folder));
    }

    [Fact]
    public void TheInstalledFolders_AreMatchedWhateverTheirCase()
    {
        string host = Touch("App", "eplanmcp.exe");

        Assert.Equal(host, HostLocator.Find(Folder("App", "Workbench")));
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void Built_TheHostIsTheOneOfTheSameConfiguration(string configuration)
    {
        string workbench = Folder("repo", "src", "EplanMcp.Workbench", "bin", configuration, "net10.0-windows");
        Touch("repo", "src", "EplanMcp.Host", "bin", configuration == "Debug" ? "Release" : "Debug", "net10.0", "eplanmcp.exe");
        Assert.Null(HostLocator.Find(workbench));

        string host = Touch("repo", "src", "EplanMcp.Host", "bin", configuration, "net10.0", "eplanmcp.exe");

        Assert.Equal(host, HostLocator.Find(workbench));
    }

    [Fact]
    public void AnythingElse_FindsNoHost_NotEvenOneNearby()
    {
        string tests = Folder("repo", "tests", "EplanMcp.Workbench.Tests", "bin", "Debug", "net10.0-windows");
        Touch("repo", "tests", "EplanMcp.Workbench.Tests", "bin", "Debug", "net10.0-windows", "eplanmcp.exe");
        Touch("repo", "src", "EplanMcp.Host", "bin", "Debug", "net10.0", "eplanmcp.exe");
        string loose = Folder("loose");

        Assert.Null(HostLocator.Find(tests));
        Assert.Null(HostLocator.Find(loose));
        Assert.Null(HostLocator.Built(loose));
    }

    [Fact]
    public void ThisRepositorysWorkbench_FindsTheHostBuiltWithIt()
    {
        // The tests run from tests\EplanMcp.Workbench.Tests\bin\<configuration>\<tfm>\, and the solution builds both.
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string configuration = output.Parent!.Name;
        string repository = output.Parent.Parent!.Parent!.Parent!.Parent!.FullName;
        string workbench = Path.Combine(repository, "src", "EplanMcp.Workbench", "bin", configuration, "net10.0-windows");

        Assert.Equal(Path.Combine(repository, "src", "EplanMcp.Host", "bin", configuration, "net10.0", "eplanmcp.exe"), HostLocator.Find(workbench));
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
